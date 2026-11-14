import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { formatTimestamp, roleClass, roleLabel } from '../../core/format';
import { WorkspaceRole } from '../../core/models';
import { describeProblem, problemDetails } from '../../core/problem';
import { WorkspaceService } from '../../core/workspace.service';
import { ProblemBanner } from '../../shared/problem-banner';
import { ToastService } from '../../shared/toast.service';
import { WorkspaceContext } from '../../shared/workspace-context';

/** Workspace details, lifecycle and the caller's effective permissions. */
@Component({
  selector: 'app-workspace-overview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ProblemBanner],
  template: `
    @if (context.detail(); as detail) {
      <app-problem-banner heading="Operation refused" [message]="problem()" [errors]="errors()" />

      <div class="split">
        <div class="stack">
          <section class="card">
            <div class="card-head">
              <h2>Workspace details</h2>
              <span class="dim small">{{
                canEdit() ? 'Editable' : 'Read-only for your role'
              }}</span>
            </div>
            <form class="card-body stack" [formGroup]="form" (ngSubmit)="save()" novalidate>
              <div class="form-grid">
                <div class="field span-2">
                  <label class="label" for="ws-name">Name</label>
                  <input
                    id="ws-name"
                    class="input"
                    type="text"
                    formControlName="name"
                    [readOnly]="!canEdit()"
                  />
                  @if (form.controls.name.touched && form.controls.name.invalid) {
                    <span class="hint">A name of 2–100 characters is required.</span>
                  }
                </div>
                <div class="field span-2">
                  <label class="label" for="ws-description">Description</label>
                  <textarea
                    id="ws-description"
                    class="textarea"
                    formControlName="description"
                    [readOnly]="!canEdit()"
                  ></textarea>
                  <span class="hint"
                    >Up to 1000 characters. Avoid secrets; descriptions are not encrypted.</span
                  >
                </div>
              </div>
              @if (canEdit()) {
                <div class="form-actions">
                  <button type="submit" class="btn btn-primary" [disabled]="saving()">
                    @if (saving()) {
                      <span class="spinner" aria-hidden="true"></span>
                    }
                    Save changes
                  </button>
                </div>
              }
            </form>
          </section>

          <section class="card">
            <div class="card-head"><h2>Assessment boundary</h2></div>
            <div class="card-body stack">
              <p class="muted small">
                TraceForge performs no assessment work in this phase. Every destination must be
                registered as an authorized scope and approved by an Owner or Administrator before
                any later module may contact it.
              </p>
              <div class="meta-row">
                <span class="badge">{{ detail.scopeCount }} scope(s)</span>
                <span class="badge">{{ detail.memberCount }} member(s)</span>
                <span
                  class="badge"
                  [class]="detail.archivedAtUtc ? 'badge-revoked' : 'badge-active'"
                >
                  {{ detail.archivedAtUtc ? 'Archived' : 'Active' }}
                </span>
              </div>
            </div>
          </section>
        </div>

        <div class="stack">
          <section class="card">
            <div class="card-head"><h2>Lifecycle</h2></div>
            <div class="card-body stack">
              <div class="field">
                <span class="label">Created</span>
                <span>{{ formatTimestamp(detail.createdAtUtc) }}</span>
              </div>
              <div class="field">
                <span class="label">Last modified</span>
                <span>{{ formatTimestamp(detail.updatedAtUtc) }}</span>
              </div>
              <div class="field">
                <span class="label">Archived</span>
                <span>{{ formatTimestamp(detail.archivedAtUtc) }}</span>
              </div>
              @if (context.canArchive()) {
                <button
                  type="button"
                  class="btn btn-danger"
                  [disabled]="archiving()"
                  (click)="archive()"
                >
                  @if (archiving()) {
                    <span class="spinner" aria-hidden="true"></span>
                  }
                  Archive workspace
                </button>
                <span class="hint">
                  Archiving is permanent for this workspace: mutations are rejected afterwards. Only
                  an Owner may archive.
                </span>
              } @else if (!context.isArchived()) {
                <span class="hint">Only an Owner may archive this workspace.</span>
              }
            </div>
          </section>

          <section class="card">
            <div class="card-head">
              <h2>Your permissions</h2>
              <span class="badge" [class]="roleClass(detail.callerRole)">{{
                roleLabel(detail.callerRole)
              }}</span>
            </div>
            <div class="card-body">
              <table class="table">
                <tbody>
                  @for (rule of permissionRules(); track rule.label) {
                    <tr>
                      <td>{{ rule.label }}</td>
                      <td class="num" [class.muted]="!rule.allowed">
                        {{ rule.allowed ? 'Allowed' : 'Denied' }}
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
              <p class="hint" style="margin-top: 10px;">
                Displayed for guidance only. The API re-evaluates every permission on every request.
              </p>
            </div>
          </section>
        </div>
      </div>
    }
  `,
})
export class OverviewPage {
  private readonly service = inject(WorkspaceService);
  private readonly toasts = inject(ToastService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly context = inject(WorkspaceContext);

  protected readonly form = this.formBuilder.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    description: ['', [Validators.maxLength(1000)]],
  });

  protected readonly saving = signal(false);
  protected readonly archiving = signal(false);
  protected readonly problem = signal<string | null>(null);
  protected readonly errors = signal<string[]>([]);

  protected readonly canEdit = computed(() => this.context.canUpdate());
  protected readonly roleClass = roleClass;
  protected readonly roleLabel = roleLabel;
  protected readonly formatTimestamp = formatTimestamp;

  protected readonly permissionRules = computed(() => {
    const role = this.context.role();
    const owner = role === WorkspaceRole.Owner;
    const manager = owner || role === WorkspaceRole.Administrator;

    return [
      { label: 'Update workspace details', allowed: manager && !this.context.isArchived() },
      { label: 'Archive workspace', allowed: owner && !this.context.isArchived() },
      { label: 'Manage members', allowed: manager && !this.context.isArchived() },
      { label: 'Grant or remove the Owner role', allowed: owner && !this.context.isArchived() },
      { label: 'Register scopes (pending)', allowed: role !== null && !this.context.isArchived() },
      {
        label: 'Approve, revoke or restrict scopes',
        allowed: manager && !this.context.isArchived(),
      },
    ];
  });

  private lastWorkspaceId: string | null = null;

  constructor() {
    // Keep the edit form in sync with the authoritative detail, including after a save reload.
    effect(() => {
      const detail = this.context.detail();
      if (detail && detail.id !== this.lastWorkspaceId) {
        this.lastWorkspaceId = detail.id;
        this.form.reset({ name: detail.name, description: detail.description ?? '' });
      }
    });
  }

  protected async save(): Promise<void> {
    const detail = this.context.detail();
    if (!detail) {
      return;
    }

    this.problem.set(null);
    this.errors.set([]);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    try {
      const { name, description } = this.form.getRawValue();
      const updated = await firstValueFrom(
        this.service.update(detail.id, {
          name: name.trim(),
          description: description.trim() ? description.trim() : null,
        }),
      );

      this.context.set(updated.id, updated);
      this.toasts.success('Workspace updated.');
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.saving.set(false);
    }
  }

  protected async archive(): Promise<void> {
    const detail = this.context.detail();
    if (!detail) {
      return;
    }

    const confirmed = window.confirm(
      `Archive "${detail.name}"? Mutations are rejected afterwards and this cannot be undone from the console.`,
    );
    if (!confirmed) {
      return;
    }

    this.problem.set(null);
    this.errors.set([]);
    this.archiving.set(true);
    try {
      const archived = await firstValueFrom(this.service.archive(detail.id));
      this.context.set(archived.id, archived);
      this.toasts.success('Workspace archived.');
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.archiving.set(false);
    }
  }
}
