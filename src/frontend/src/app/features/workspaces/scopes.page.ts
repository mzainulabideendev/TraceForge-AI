import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import {
  daysFromNow,
  formatTimestamp,
  isPast,
  scopeTypeLabel,
  statusClass,
  toDateTimeLocal,
  toUtcIso,
} from '../../core/format';
import { Scope } from '../../core/models';
import { describeProblem, problemDetails } from '../../core/problem';
import { ScopeService } from '../../core/scope.service';
import { ProblemBanner } from '../../shared/problem-banner';
import { ToastService } from '../../shared/toast.service';
import { WorkspaceContext } from '../../shared/workspace-context';

const SCOPE_TYPES = [
  { value: 'Origin', label: 'HTTP(S) origin' },
  { value: 'Hostname', label: 'DNS hostname' },
  { value: 'IPv4', label: 'IPv4 address' },
  { value: 'IPv6', label: 'IPv6 address' },
  { value: 'Cidr', label: 'CIDR range' },
] as const;

const STATUSES = ['All', 'Pending', 'Approved', 'Revoked', 'Expired'] as const;
type StatusFilter = (typeof STATUSES)[number];

type ActionKind = 'approve' | 'revoke' | 'restrictions';

/** Authorized-scope register, approval lifecycle and restriction management. */
@Component({
  selector: 'app-workspace-scopes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ProblemBanner],
  template: `
    <app-problem-banner
      heading="Scope operation refused"
      [message]="problem()"
      [errors]="errors()"
    />

    <div class="page-head" style="margin-bottom: 14px;">
      <div class="page-head-text">
        <h2>Authorized scopes</h2>
        <p class="lede">
          Registration never approves a target. An Owner or Administrator must approve each scope
          with a written authorization reference before it can be used.
        </p>
      </div>
      <div class="page-actions">
        <button
          type="button"
          class="btn btn-primary"
          (click)="toggleRegister()"
          [disabled]="context.isArchived()"
        >
          {{ registerOpen() ? 'Cancel' : 'Register scope' }}
        </button>
      </div>
    </div>

    @if (registerOpen()) {
      <section class="card">
        <div class="card-head">
          <h2>Register a scope</h2>
          <span class="badge badge-pending">Stored as Pending</span>
        </div>
        <form class="card-body stack" [formGroup]="registerForm" (ngSubmit)="register()" novalidate>
          <div class="form-grid">
            <div class="field">
              <label class="label" for="scope-type">Type</label>
              <select id="scope-type" class="select" formControlName="type">
                @for (option of scopeTypes; track option.value) {
                  <option [value]="option.value">{{ option.label }}</option>
                }
              </select>
            </div>
            <div class="field">
              <label class="label" for="scope-target">Target</label>
              <input
                id="scope-target"
                class="input mono"
                type="text"
                formControlName="target"
                placeholder="https://app.example.com:443"
              />
              @if (registerForm.controls.target.touched && registerForm.controls.target.invalid) {
                <span class="hint">A target is required.</span>
              }
            </div>
            <div class="field">
              <label class="label" for="valid-from">Valid from (local time)</label>
              <input
                id="valid-from"
                class="input"
                type="datetime-local"
                formControlName="validFrom"
              />
            </div>
            <div class="field">
              <label class="label" for="expires-at">Expires at (local time)</label>
              <input
                id="expires-at"
                class="input"
                type="datetime-local"
                formControlName="expiresAt"
              />
              @if (expiryProblem()) {
                <span class="hint">{{ expiryProblem() }}</span>
              }
            </div>
            <div class="field span-2">
              <label class="check">
                <input type="checkbox" formControlName="privateNetwork" />
                <span>
                  Permit private / loopback / link-local destinations (<code>private-network</code>
                  restriction)
                </span>
              </label>
              <span class="hint">
                Still requires the deployment switch
                <code>TraceForge:Scopes:AllowPrivateNetworks</code> to be enabled; without both, the
                destination stays denied.
              </span>
            </div>
          </div>

          <div class="form-actions">
            <button type="button" class="btn btn-ghost" (click)="toggleRegister()">Cancel</button>
            <button type="submit" class="btn btn-primary" [disabled]="registering()">
              @if (registering()) {
                <span class="spinner" aria-hidden="true"></span>
              }
              Register as Pending
            </button>
          </div>
        </form>
      </section>
    }

    <section class="card">
      <div class="card-head">
        <h2>Scopes</h2>
        <div class="pill-row">
          @for (status of statuses; track status) {
            <button
              type="button"
              class="btn btn-sm"
              [class.btn-primary]="statusFilter() === status"
              [class.btn-ghost]="statusFilter() !== status"
              (click)="statusFilter.set(status)"
            >
              {{ status }} ({{ countFor(status) }})
            </button>
          }
        </div>
      </div>

      <div class="card-body" style="border-bottom: 1px solid var(--border);">
        <div class="field">
          <label class="label" for="scope-filter">Filter targets</label>
          <input
            id="scope-filter"
            class="input"
            type="search"
            placeholder="Search by target, type or approval reference"
            [value]="filter()"
            (input)="filter.set($any($event.target).value)"
          />
        </div>
      </div>

      @if (loading()) {
        <div class="loading-row">
          <span class="spinner" aria-hidden="true"></span> Loading scopes…
        </div>
      } @else if (scopes().length === 0) {
        <div class="empty">
          <strong>No scopes registered</strong>
          <span
            >Register an origin, hostname, address or CIDR range to start the approval
            workflow.</span
          >
        </div>
      } @else if (visibleScopes().length === 0) {
        <div class="empty">
          <strong>No matching scopes</strong><span>Adjust the status or text filter.</span>
        </div>
      } @else {
        <div class="table-wrap" style="border: 0; border-radius: 0;">
          <table class="table">
            <thead>
              <tr>
                <th>Type</th>
                <th>Target</th>
                <th>Status</th>
                <th>Usable</th>
                <th>Validity</th>
                <th>Restrictions</th>
                @if (context.canApproveScopes()) {
                  <th style="text-align: right;">Actions</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (scope of visibleScopes(); track scope.id) {
                <tr>
                  <td class="nowrap">{{ scopeTypeLabel(scope.type) }}</td>
                  <td class="cell-target mono">{{ scope.target }}</td>
                  <td>
                    <span class="badge" [class]="statusClass(scope.status)">{{
                      scope.status
                    }}</span>
                  </td>
                  <td>
                    @if (scope.currentlyUsable) {
                      <span class="badge badge-active">Yes</span>
                    } @else {
                      <span class="badge">No</span>
                    }
                  </td>
                  <td class="num">
                    <div>{{ formatTimestamp(scope.validFromUtc) }}</div>
                    <div class="dim">to {{ formatTimestamp(scope.expiresAtUtc) }}</div>
                  </td>
                  <td>
                    @if (scope.restrictions.length === 0) {
                      <span class="dim">None</span>
                    } @else {
                      <div class="pill-row">
                        @for (restriction of scope.restrictions; track restriction) {
                          <span class="badge badge-warning">{{ restriction }}</span>
                        }
                      </div>
                    }
                  </td>
                  @if (context.canApproveScopes()) {
                    <td>
                      <div class="row-actions">
                        @if (scope.status === 'Pending') {
                          <button
                            type="button"
                            class="btn btn-primary btn-sm"
                            (click)="openAction(scope, 'approve')"
                          >
                            Approve
                          </button>
                        }
                        @if (scope.status === 'Approved' && isPast(scope.expiresAtUtc)) {
                          <button
                            type="button"
                            class="btn btn-ghost btn-sm"
                            [disabled]="busyId() === scope.id"
                            (click)="expire(scope)"
                          >
                            Expire
                          </button>
                        }
                        @if (scope.status !== 'Revoked') {
                          <button
                            type="button"
                            class="btn btn-ghost btn-sm"
                            (click)="openAction(scope, 'revoke')"
                          >
                            Revoke
                          </button>
                          <button
                            type="button"
                            class="btn btn-ghost btn-sm"
                            (click)="openAction(scope, 'restrictions')"
                          >
                            Restrictions
                          </button>
                        }
                      </div>
                    </td>
                  }
                </tr>

                @if (activeAction(); as current) {
                  @if (current.scopeId === scope.id) {
                    <tr class="row-detail">
                      <td [attr.colspan]="context.canApproveScopes() ? 7 : 6">
                        @switch (current.kind) {
                          @case ('approve') {
                            <div class="inline-form">
                              <div class="field">
                                <label class="label" [for]="'ref-' + scope.id">
                                  Authorization reference (3–500 characters)
                                </label>
                                <input
                                  [id]="'ref-' + scope.id"
                                  class="input"
                                  type="text"
                                  placeholder="CHANGE-1234 / ticket or written approval id"
                                  [value]="approvalReference()"
                                  (input)="approvalReference.set($any($event.target).value)"
                                />
                                <span class="hint">
                                  Stored as approval evidence and audited. Approval makes this scope
                                  usable immediately.
                                </span>
                              </div>
                              <button
                                type="button"
                                class="btn btn-primary"
                                [disabled]="
                                  approvalReference().trim().length < 3 || busyId() === scope.id
                                "
                                (click)="approve(scope)"
                              >
                                Confirm approval
                              </button>
                              <button type="button" class="btn btn-ghost" (click)="closeAction()">
                                Cancel
                              </button>
                            </div>
                          }
                          @case ('revoke') {
                            <div class="inline-form">
                              <div class="field">
                                <label class="label" [for]="'reason-' + scope.id"
                                  >Reason (optional)</label
                                >
                                <input
                                  [id]="'reason-' + scope.id"
                                  class="input"
                                  type="text"
                                  placeholder="Why is this scope being revoked?"
                                  [value]="revokeReason()"
                                  (input)="revokeReason.set($any($event.target).value)"
                                />
                                <span class="hint"
                                  >Revocation is immediate. A revoked scope cannot be
                                  re-approved.</span
                                >
                              </div>
                              <button
                                type="button"
                                class="btn btn-danger"
                                [disabled]="busyId() === scope.id"
                                (click)="revoke(scope)"
                              >
                                Confirm revocation
                              </button>
                              <button type="button" class="btn btn-ghost" (click)="closeAction()">
                                Cancel
                              </button>
                            </div>
                          }
                          @case ('restrictions') {
                            <div class="inline-form">
                              <label class="check">
                                <input
                                  type="checkbox"
                                  [checked]="restrictionPrivate()"
                                  (change)="restrictionPrivate.set($any($event.target).checked)"
                                />
                                <span><code>private-network</code></span>
                              </label>
                              <button
                                type="button"
                                class="btn btn-primary"
                                [disabled]="busyId() === scope.id"
                                (click)="saveRestrictions(scope)"
                              >
                                Save restrictions
                              </button>
                              <button type="button" class="btn btn-ghost" (click)="closeAction()">
                                Cancel
                              </button>
                              <span class="hint">
                                Saving restrictions resets this scope to Pending and clears its
                                approval.
                              </span>
                            </div>
                          }
                        }
                      </td>
                    </tr>
                  }
                }
              }
            </tbody>
          </table>
        </div>
      }

      <div class="card-body" style="border-top: 1px solid var(--border);">
        <p class="hint">
          Approving a hostname never approves the addresses it resolves to: a resolved address must
          match its own approved IP or CIDR scope. Use the Evaluate tab to test a destination
          without opening a connection.
        </p>
      </div>
    </section>
  `,
})
export class ScopesPage {
  private readonly service = inject(ScopeService);
  private readonly toasts = inject(ToastService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly context = inject(WorkspaceContext);

  protected readonly scopes = signal<Scope[]>([]);
  protected readonly loading = signal(true);
  protected readonly problem = signal<string | null>(null);
  protected readonly errors = signal<string[]>([]);

  protected readonly registerOpen = signal(false);
  protected readonly registering = signal(false);
  protected readonly filter = signal('');
  protected readonly statusFilter = signal<StatusFilter>('All');

  protected readonly activeAction = signal<{ scopeId: string; kind: ActionKind } | null>(null);
  protected readonly approvalReference = signal('');
  protected readonly revokeReason = signal('');
  protected readonly restrictionPrivate = signal(false);
  protected readonly busyId = signal<string | null>(null);

  protected readonly scopeTypes = SCOPE_TYPES;
  protected readonly statuses = STATUSES;

  protected readonly scopeTypeLabel = scopeTypeLabel;
  protected readonly statusClass = statusClass;
  protected readonly formatTimestamp = formatTimestamp;
  protected readonly isPast = isPast;

  protected readonly registerForm = this.formBuilder.group({
    type: ['Origin', [Validators.required]],
    target: ['', [Validators.required, Validators.maxLength(512)]],
    validFrom: [toDateTimeLocal(new Date())],
    expiresAt: [toDateTimeLocal(daysFromNow(90))],
    privateNetwork: [false],
  });

  protected readonly visibleScopes = computed(() => {
    const status = this.statusFilter();
    const text = this.filter().trim().toLowerCase();

    return this.scopes().filter((scope) => {
      const statusOk = status === 'All' || scope.status === status;
      const textOk =
        text.length === 0 ||
        scope.target.toLowerCase().includes(text) ||
        scopeTypeLabel(scope.type).toLowerCase().includes(text) ||
        (scope.approvalReference ?? '').toLowerCase().includes(text);

      return statusOk && textOk;
    });
  });

  protected readonly expiryProblem = computed(() => {
    const raw = this.registerForm.controls.expiresAt.value;
    const expiry = toUtcIso(raw);
    if (!expiry) {
      return 'Choose an expiration time.';
    }

    return new Date(expiry).getTime() <= Date.now()
      ? 'The expiration time must be in the future.'
      : null;
  });

  constructor() {
    void this.load();
  }

  protected countFor(status: StatusFilter): number {
    return status === 'All'
      ? this.scopes().length
      : this.scopes().filter((scope) => scope.status === status).length;
  }

  protected toggleRegister(): void {
    this.registerOpen.update((open) => !open);
  }

  protected openAction(scope: Scope, kind: ActionKind): void {
    this.approvalReference.set('');
    this.revokeReason.set('');
    this.restrictionPrivate.set(scope.restrictions.includes('private-network'));
    this.activeAction.set({ scopeId: scope.id, kind });
  }

  protected closeAction(): void {
    this.activeAction.set(null);
  }

  protected async register(): Promise<void> {
    const workspaceId = this.context.workspaceId();
    if (!workspaceId) {
      return;
    }

    this.problem.set(null);
    this.errors.set([]);

    if (this.registerForm.invalid) {
      this.registerForm.markAllAsTouched();
      return;
    }

    const { type, target, validFrom, expiresAt, privateNetwork } = this.registerForm.getRawValue();
    const expiry = toUtcIso(expiresAt);
    if (!expiry) {
      this.problem.set('Choose a valid expiration time.');
      return;
    }

    // The API accepts a valid-from up to one minute in the past; when the operator keeps the default
    // "now", omit it so the server stamps its own current time and no clock skew can reject the call.
    const start = toUtcIso(validFrom);
    const validFromUtc = start && new Date(start).getTime() > Date.now() + 1000 ? start : undefined;

    this.registering.set(true);
    try {
      const created = await firstValueFrom(
        this.service.register(workspaceId, {
          type,
          target: target.trim(),
          validFromUtc,
          expiresAtUtc: expiry,
          restrictions: privateNetwork ? ['private-network'] : [],
        }),
      );

      this.scopes.update((current) => [created, ...current]);
      this.registerForm.patchValue({ target: '' });
      this.registerOpen.set(false);
      this.toasts.success('Scope registered as Pending. Approval is a separate step.');
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.registering.set(false);
    }
  }

  protected async approve(scope: Scope): Promise<void> {
    const workspaceId = this.context.workspaceId();
    if (!workspaceId) {
      return;
    }

    this.problem.set(null);
    this.errors.set([]);
    this.busyId.set(scope.id);
    try {
      const approved = await firstValueFrom(
        this.service.approve(workspaceId, scope.id, {
          approvalReference: this.approvalReference().trim(),
        }),
      );

      this.replace(approved);
      this.closeAction();
      this.toasts.success(`Scope ${approved.target} approved.`);
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.busyId.set(null);
    }
  }

  protected async revoke(scope: Scope): Promise<void> {
    const workspaceId = this.context.workspaceId();
    if (!workspaceId) {
      return;
    }

    this.problem.set(null);
    this.errors.set([]);
    this.busyId.set(scope.id);
    try {
      const reason = this.revokeReason().trim();
      const revoked = await firstValueFrom(
        this.service.revoke(workspaceId, scope.id, { reason: reason.length > 0 ? reason : null }),
      );

      this.replace(revoked);
      this.closeAction();
      this.toasts.success(`Scope ${revoked.target} revoked.`);
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.busyId.set(null);
    }
  }

  protected async expire(scope: Scope): Promise<void> {
    const workspaceId = this.context.workspaceId();
    if (!workspaceId) {
      return;
    }

    this.problem.set(null);
    this.errors.set([]);
    this.busyId.set(scope.id);
    try {
      const expired = await firstValueFrom(this.service.expire(workspaceId, scope.id));
      this.replace(expired);
      this.toasts.success(`Scope ${expired.target} marked expired.`);
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.busyId.set(null);
    }
  }

  protected async saveRestrictions(scope: Scope): Promise<void> {
    const workspaceId = this.context.workspaceId();
    if (!workspaceId) {
      return;
    }

    this.problem.set(null);
    this.errors.set([]);
    this.busyId.set(scope.id);
    try {
      const updated = await firstValueFrom(
        this.service.updateRestrictions(workspaceId, scope.id, {
          restrictions: this.restrictionPrivate() ? ['private-network'] : [],
        }),
      );

      this.replace(updated);
      this.closeAction();
      this.toasts.success('Restrictions saved. The scope is Pending until it is approved again.');
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.busyId.set(null);
    }
  }

  private async load(): Promise<void> {
    const workspaceId = this.context.workspaceId();
    if (!workspaceId) {
      return;
    }

    this.loading.set(true);
    this.problem.set(null);
    try {
      this.scopes.set(await firstValueFrom(this.service.list(workspaceId)));
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.loading.set(false);
    }
  }

  private replace(updated: Scope): void {
    this.scopes.update((current) =>
      current.map((entry) => (entry.id === updated.id ? updated : entry)),
    );
  }
}
