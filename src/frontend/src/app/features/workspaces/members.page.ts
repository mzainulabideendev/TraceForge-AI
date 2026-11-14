import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { formatTimestamp, roleClass, roleLabel } from '../../core/format';
import { WorkspaceMember, WorkspaceRole } from '../../core/models';
import { describeProblem, problemDetails } from '../../core/problem';
import { SessionStore } from '../../core/session.store';
import { WorkspaceService } from '../../core/workspace.service';
import { ProblemBanner } from '../../shared/problem-banner';
import { ToastService } from '../../shared/toast.service';
import { WorkspaceContext } from '../../shared/workspace-context';

/**
 * Membership management. Owner and Administrator rules are enforced by the API; this view only
 * hides controls that are certain to be refused and renders the refusals it receives.
 */
@Component({
  selector: 'app-workspace-members',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ProblemBanner],
  template: `
    <app-problem-banner
      heading="Membership operation refused"
      [message]="problem()"
      [errors]="errors()"
    />

    @if (context.canManageMembers()) {
      <section class="card">
        <div class="card-head">
          <h2>Add member</h2>
          <span class="dim small">The account must already exist.</span>
        </div>
        <form class="card-body" [formGroup]="addForm" (ngSubmit)="add()" novalidate>
          <div class="inline-form">
            <div class="field">
              <label class="label" for="member-email">E-mail</label>
              <input
                id="member-email"
                class="input"
                type="email"
                formControlName="email"
                placeholder="analyst@example.test"
              />
            </div>
            <div class="field">
              <label class="label" for="member-role">Role</label>
              <select id="member-role" class="select" formControlName="role">
                @for (role of grantableRoles(); track role) {
                  <option [value]="role">{{ role }}</option>
                }
              </select>
            </div>
            <button type="submit" class="btn btn-primary" [disabled]="adding()">
              @if (adding()) {
                <span class="spinner" aria-hidden="true"></span>
              }
              Add member
            </button>
          </div>
          @if (!context.canAssignOwner()) {
            <p class="hint" style="margin-top: 10px;">
              Administrators can add Analysts and Administrators. Only an Owner can grant the Owner
              role.
            </p>
          }
        </form>
      </section>
    }

    <section class="card">
      <div class="card-head">
        <h2>Members</h2>
        <span class="dim small">{{ members().length }} total</span>
      </div>

      @if (loading()) {
        <div class="loading-row">
          <span class="spinner" aria-hidden="true"></span> Loading members…
        </div>
      } @else if (members().length === 0) {
        <div class="empty">
          <strong>No members</strong><span>This should not happen for an active workspace.</span>
        </div>
      } @else {
        <div class="table-wrap" style="border: 0; border-radius: 0;">
          <table class="table">
            <thead>
              <tr>
                <th>Member</th>
                <th>Role</th>
                <th>Added</th>
                <th>Updated</th>
                @if (context.canManageMembers()) {
                  <th style="text-align: right;">Actions</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (member of members(); track member.userId) {
                <tr>
                  <td>
                    <div>
                      <strong>{{ member.displayName || member.email }}</strong>
                      @if (member.userId === currentUserId()) {
                        <span class="dim small"> (you)</span>
                      }
                    </div>
                    <div class="dim small mono">{{ member.email }}</div>
                  </td>
                  <td>
                    <span class="badge" [class]="roleClass(member.role)">{{
                      roleLabel(member.role)
                    }}</span>
                  </td>
                  <td class="num">{{ formatTimestamp(member.addedAtUtc) }}</td>
                  <td class="num">{{ formatTimestamp(member.updatedAtUtc) }}</td>
                  @if (context.canManageMembers()) {
                    <td>
                      <div class="row-actions">
                        <select
                          class="select"
                          style="width: auto; padding: 4px 30px 4px 8px;"
                          [value]="draftRole(member)"
                          [disabled]="savingId() === member.userId"
                          (change)="setDraft(member.userId, $any($event.target).value)"
                          [attr.aria-label]="'Role for ' + member.email"
                        >
                          @for (role of assignableRoles(member); track role) {
                            <option [value]="role">{{ role }}</option>
                          }
                        </select>
                        <button
                          type="button"
                          class="btn btn-ghost btn-sm"
                          [disabled]="!isDirty(member) || savingId() === member.userId"
                          (click)="applyRole(member)"
                        >
                          Apply
                        </button>
                        <button
                          type="button"
                          class="btn btn-danger btn-sm"
                          [disabled]="removingId() === member.userId"
                          (click)="remove(member)"
                        >
                          Remove
                        </button>
                      </div>
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
      <div class="card-body" style="border-top: 1px solid var(--border);">
        <p class="hint">
          The last Owner cannot be demoted or removed. An Administrator cannot modify an Owner or
          grant the Owner role.
        </p>
      </div>
    </section>
  `,
})
export class MembersPage {
  private readonly service = inject(WorkspaceService);
  private readonly toasts = inject(ToastService);
  private readonly session = inject(SessionStore);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly context = inject(WorkspaceContext);

  protected readonly members = signal<WorkspaceMember[]>([]);
  protected readonly loading = signal(true);
  protected readonly problem = signal<string | null>(null);
  protected readonly errors = signal<string[]>([]);

  protected readonly adding = signal(false);
  protected readonly savingId = signal<string | null>(null);
  protected readonly removingId = signal<string | null>(null);
  protected readonly drafts = signal<Record<string, string>>({});

  protected readonly addForm = this.formBuilder.group({
    email: ['', [Validators.required, Validators.email]],
    role: ['Analyst', [Validators.required]],
  });

  protected readonly roleLabel = roleLabel;
  protected readonly roleClass = roleClass;
  protected readonly formatTimestamp = formatTimestamp;
  protected readonly currentUserId = computed(() => this.session.user()?.id ?? null);

  protected readonly grantableRoles = computed(() =>
    this.context.canAssignOwner()
      ? ['Analyst', 'Administrator', 'Owner']
      : ['Analyst', 'Administrator'],
  );

  constructor() {
    void this.load();
  }

  /** Options for an existing row: an owner keeps the Owner option, everyone else gets safe choices. */
  protected assignableRoles(member: WorkspaceMember): string[] {
    if (member.role === WorkspaceRole.Owner || this.context.canAssignOwner()) {
      return ['Analyst', 'Administrator', 'Owner'];
    }

    return ['Analyst', 'Administrator'];
  }

  protected draftRole(member: WorkspaceMember): string {
    return this.drafts()[member.userId] ?? roleLabel(member.role);
  }

  protected setDraft(userId: string, role: string): void {
    this.drafts.update((current) => ({ ...current, [userId]: role }));
  }

  protected isDirty(member: WorkspaceMember): boolean {
    return this.draftRole(member) !== roleLabel(member.role);
  }

  protected async add(): Promise<void> {
    const workspaceId = this.context.workspaceId();
    if (!workspaceId) {
      return;
    }

    this.problem.set(null);
    this.errors.set([]);

    if (this.addForm.invalid) {
      this.addForm.markAllAsTouched();
      return;
    }

    this.adding.set(true);
    try {
      const { email, role } = this.addForm.getRawValue();
      const created = await firstValueFrom(
        this.service.addMember(workspaceId, { email: email.trim(), role }),
      );

      this.members.update((current) => [...current, created]);
      this.addForm.reset({ email: '', role: 'Analyst' });
      this.toasts.success(`${created.email} added as ${roleLabel(created.role)}.`);
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.adding.set(false);
    }
  }

  protected async applyRole(member: WorkspaceMember): Promise<void> {
    const workspaceId = this.context.workspaceId();
    if (!workspaceId) {
      return;
    }

    this.problem.set(null);
    this.errors.set([]);
    this.savingId.set(member.userId);
    try {
      const updated = await firstValueFrom(
        this.service.changeMemberRole(workspaceId, member.userId, { role: this.draftRole(member) }),
      );

      this.replace(updated);
      this.drafts.update((current) => {
        const next = { ...current };
        delete next[member.userId];
        return next;
      });
      this.toasts.success(`${updated.email} is now ${roleLabel(updated.role)}.`);
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.savingId.set(null);
    }
  }

  protected async remove(member: WorkspaceMember): Promise<void> {
    const workspaceId = this.context.workspaceId();
    if (!workspaceId) {
      return;
    }

    const confirmed = window.confirm(
      `Remove ${member.email} from this workspace? They lose all access immediately.`,
    );
    if (!confirmed) {
      return;
    }

    this.problem.set(null);
    this.errors.set([]);
    this.removingId.set(member.userId);
    try {
      await firstValueFrom(this.service.removeMember(workspaceId, member.userId));
      this.members.update((current) => current.filter((entry) => entry.userId !== member.userId));
      this.toasts.success(`${member.email} removed.`);
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.removingId.set(null);
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
      this.members.set(await firstValueFrom(this.service.listMembers(workspaceId)));
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.loading.set(false);
    }
  }

  private replace(updated: WorkspaceMember): void {
    this.members.update((current) =>
      current.map((entry) => (entry.userId === updated.userId ? updated : entry)),
    );
  }
}
