import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { scopeTypeLabel } from '../../core/format';
import { Scope, ScopeEvaluation } from '../../core/models';
import { describeProblem, problemDetails } from '../../core/problem';
import { ScopeService } from '../../core/scope.service';
import { ProblemBanner } from '../../shared/problem-banner';
import { WorkspaceContext } from '../../shared/workspace-context';

const DENIAL_REASONS: Record<string, string> = {
  MalformedTarget: 'The target is not a valid address, hostname or origin.',
  NoApprovedScope: 'No approved scope covers this destination.',
  ScopePending: 'A matching scope exists but has not been approved.',
  ScopeExpired: 'The matching scope is past its expiration time.',
  ScopeRevoked: 'The matching scope was revoked.',
  SchemeOrPortNotApproved: 'The scheme or port is not covered by an approved origin scope.',
  PrivateNetworkBlocked:
    'Private, loopback, link-local or reserved destinations are denied by policy.',
  ResolvedAddressNotApproved:
    'A resolved address is not covered by its own approved IP or CIDR scope.',
  DnsResolutionFailed: 'The hostname could not be resolved.',
  RedirectOutsideScope: 'The redirect leaves the approved origin.',
};

/**
 * Evaluates a destination against the workspace's approved scopes. The API performs no connection,
 * request or scan; this screen is a decision preview, not a probe.
 */
@Component({
  selector: 'app-workspace-evaluate',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ProblemBanner],
  template: `
    <app-problem-banner heading="Evaluation failed" [message]="problem()" [errors]="errors()" />

    <div class="split">
      <div class="stack">
        <section class="card">
          <div class="card-head">
            <h2>Evaluate a destination</h2>
            <span class="dim small">No network connection is made</span>
          </div>
          <form class="card-body stack" [formGroup]="form" (ngSubmit)="evaluate()" novalidate>
            <div class="form-grid">
              <div class="field span-2">
                <label class="label" for="eval-target">Target</label>
                <input
                  id="eval-target"
                  class="input mono"
                  type="text"
                  formControlName="target"
                  placeholder="https://app.example.com or 203.0.113.10"
                />
                @if (form.controls.target.touched && form.controls.target.invalid) {
                  <span class="hint">Enter the destination you want the policy to check.</span>
                }
              </div>
              <div class="field">
                <label class="label" for="eval-scheme">Scheme</label>
                <select id="eval-scheme" class="select" formControlName="scheme">
                  <option value="">Auto / not specified</option>
                  <option value="https">https</option>
                  <option value="http">http</option>
                </select>
              </div>
              <div class="field">
                <label class="label" for="eval-port">Port (optional)</label>
                <input
                  id="eval-port"
                  class="input"
                  type="number"
                  min="1"
                  max="65535"
                  formControlName="port"
                />
              </div>
            </div>
            <div class="form-actions">
              <button type="submit" class="btn btn-primary" [disabled]="busy()">
                @if (busy()) {
                  <span class="spinner" aria-hidden="true"></span>
                }
                Evaluate
              </button>
            </div>
          </form>
        </section>

        @if (result(); as evaluation) {
          <section class="card">
            <div class="card-head">
              <h2>Decision</h2>
              <span class="badge" [class]="evaluation.allowed ? 'badge-active' : 'badge-revoked'">
                {{ evaluation.allowed ? 'Allowed' : 'Denied' }}
              </span>
            </div>
            <div class="card-body stack">
              <div
                class="alert"
                [class.alert-success]="evaluation.allowed"
                [class.alert-error]="!evaluation.allowed"
              >
                <div>
                  <div class="alert-title">
                    {{
                      evaluation.allowed
                        ? 'Destination is authorized'
                        : 'Destination is not authorized'
                    }}
                  </div>
                  <div>{{ evaluation.detail }}</div>
                </div>
              </div>

              <div class="form-grid">
                <div class="field">
                  <span class="label">Reason code</span>
                  <span class="mono">{{ evaluation.reason }}</span>
                  @if (reasonHelp(evaluation.reason); as help) {
                    <span class="hint">{{ help }}</span>
                  }
                </div>
                <div class="field">
                  <span class="label">Matched scope</span>
                  <span class="mono">{{ evaluation.matchedScopeId || '—' }}</span>
                </div>
              </div>

              <div class="field">
                <span class="label">Pinned addresses</span>
                @if (evaluation.pinnedAddresses.length === 0) {
                  <span class="dim"
                    >None. A caller must not connect to an unpinned destination.</span
                  >
                } @else {
                  <div class="pill-row">
                    @for (address of evaluation.pinnedAddresses; track address) {
                      <span class="badge badge-active mono">{{ address }}</span>
                    }
                  </div>
                  <span class="hint">
                    Any later connector must dial these exact addresses and must not re-resolve the
                    hostname.
                  </span>
                }
              </div>
            </div>
          </section>
        }
      </div>

      <div class="stack">
        <section class="card">
          <div class="card-head"><h2>How the decision is made</h2></div>
          <div class="card-body stack small muted">
            <p>1. Your workspace membership is resolved server-side.</p>
            <p>2. The target is normalized; malformed input is denied.</p>
            <p>3. Scopes are matched by explicit type, never by guesswork.</p>
            <p>
              4. Origins additionally require an approved hostname and approved resolved addresses.
            </p>
            <p>5. Pending, expired and revoked scopes are denied with distinct reasons.</p>
            <p>
              6. Private and reserved addresses stay blocked unless both the deployment switch and
              the scope restriction allow them.
            </p>
          </div>
        </section>

        <section class="card">
          <div class="card-head">
            <h2>Usable scopes</h2>
            <span class="dim small">{{ usableScopes().length }}</span>
          </div>
          <div class="card-body">
            @if (usableScopes().length === 0) {
              <p class="dim small">
                No scope is currently approved and inside its validity window. Approve one on the
                Authorized scopes tab.
              </p>
            } @else {
              <div class="pill-row">
                @for (scope of usableScopes(); track scope.id) {
                  <button type="button" class="btn btn-ghost btn-sm mono" (click)="prefill(scope)">
                    {{ scope.target }}
                  </button>
                }
              </div>
              <p class="hint" style="margin-top: 10px;">Select a scope to prefill the target.</p>
            }
          </div>
        </section>
      </div>
    </div>
  `,
})
export class EvaluatePage {
  private readonly service = inject(ScopeService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly context = inject(WorkspaceContext);

  protected readonly form = this.formBuilder.group({
    target: ['', [Validators.required, Validators.maxLength(2048)]],
    scheme: [''],
    port: [''],
  });

  protected readonly busy = signal(false);
  protected readonly result = signal<ScopeEvaluation | null>(null);
  protected readonly problem = signal<string | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly scopes = signal<Scope[]>([]);

  protected readonly usableScopes = computed(() =>
    this.scopes().filter((scope) => scope.currentlyUsable),
  );

  protected readonly scopeTypeLabel = scopeTypeLabel;

  constructor() {
    void this.loadScopes();
  }

  protected reasonHelp(reason: string): string {
    return DENIAL_REASONS[reason] ?? '';
  }

  protected prefill(scope: Scope): void {
    this.form.patchValue({ target: scope.target, scheme: '', port: '' });
  }

  protected async evaluate(): Promise<void> {
    const workspaceId = this.context.workspaceId();
    if (!workspaceId) {
      return;
    }

    this.problem.set(null);
    this.errors.set([]);
    this.result.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { target, scheme, port } = this.form.getRawValue();
    const parsedPort = port.trim().length > 0 ? Number.parseInt(port, 10) : null;

    if (parsedPort !== null && (Number.isNaN(parsedPort) || parsedPort < 1 || parsedPort > 65535)) {
      this.problem.set('The port must be a number between 1 and 65535.');
      return;
    }

    this.busy.set(true);
    try {
      this.result.set(
        await firstValueFrom(
          this.service.evaluate(workspaceId, {
            target: target.trim(),
            scheme: scheme.length > 0 ? scheme : null,
            port: parsedPort,
          }),
        ),
      );
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async loadScopes(): Promise<void> {
    const workspaceId = this.context.workspaceId();
    if (!workspaceId) {
      return;
    }

    try {
      this.scopes.set(await firstValueFrom(this.service.list(workspaceId)));
    } catch {
      // The evaluation form still works when the list cannot be loaded; the action reports the error.
    }
  }
}
