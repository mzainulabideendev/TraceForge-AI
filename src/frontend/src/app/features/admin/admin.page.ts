import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { auditOutcomeLabel, formatTimestamp, statusClass } from '../../core/format';
import { AuditEntry, PagedResult, UserAccount } from '../../core/models';
import { describeProblem, problemDetails } from '../../core/problem';
import { AdminService } from '../../core/scope.service';
import { ProblemBanner } from '../../shared/problem-banner';

type AdminTab = 'users' | 'audit';

/** Global administration: account list and the append-only security audit log. */
@Component({
  selector: 'app-admin-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ProblemBanner],
  template: `
    <div class="page-head">
      <div class="page-head-text">
        <span class="eyebrow">Administration</span>
        <h1>Instance administration</h1>
        <p class="lede">
          Visible to global Administrators only. The API rejects these queries with 403 for every
          other account.
        </p>
      </div>
    </div>

    <div class="stack">
      <app-problem-banner
        heading="Administration query refused"
        [message]="problem()"
        [errors]="errors()"
      />

      <nav class="tabs" aria-label="Administration sections">
        <button
          type="button"
          class="tab"
          [class.active]="tab() === 'users'"
          (click)="show('users')"
        >
          Accounts
        </button>
        <button
          type="button"
          class="tab"
          [class.active]="tab() === 'audit'"
          (click)="show('audit')"
        >
          Audit log
        </button>
      </nav>

      @if (tab() === 'users') {
        <section class="card">
          <div class="card-head">
            <h2>Accounts</h2>
            @if (usersPage(); as page) {
              <span class="dim small">{{ page.totalCount }} account(s)</span>
            }
          </div>

          @if (loading()) {
            <div class="loading-row">
              <span class="spinner" aria-hidden="true"></span> Loading accounts…
            </div>
          } @else if (usersPage(); as page) {
            <div class="table-wrap" style="border: 0; border-radius: 0;">
              <table class="table">
                <thead>
                  <tr>
                    <th>Account</th>
                    <th>Global roles</th>
                    <th>Created</th>
                    <th>Last sign-in</th>
                  </tr>
                </thead>
                <tbody>
                  @for (user of page.items; track user.id) {
                    <tr>
                      <td>
                        <div>
                          <strong>{{ user.displayName || user.email }}</strong>
                        </div>
                        <div class="dim small mono">{{ user.email }}</div>
                      </td>
                      <td>
                        <div class="pill-row">
                          @for (role of user.roles; track role) {
                            <span class="badge badge-administrator">{{ role }}</span>
                          }
                          @if (user.roles.length === 0) {
                            <span class="dim">None</span>
                          }
                        </div>
                      </td>
                      <td class="num">{{ formatTimestamp(user.createdAtUtc) }}</td>
                      <td class="num">{{ formatTimestamp(user.lastLoginAtUtc) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <div
              class="card-body"
              style="display: flex; justify-content: space-between; align-items: center; gap: 12px;"
            >
              <span class="dim small">Page {{ page.page }} of {{ page.totalPages || 1 }}</span>
              <div class="page-actions">
                <button
                  type="button"
                  class="btn btn-ghost btn-sm"
                  [disabled]="!page.hasPrevious"
                  (click)="loadUsers(page.page - 1)"
                >
                  Previous
                </button>
                <button
                  type="button"
                  class="btn btn-ghost btn-sm"
                  [disabled]="!page.hasNext"
                  (click)="loadUsers(page.page + 1)"
                >
                  Next
                </button>
              </div>
            </div>
          }
        </section>
      } @else {
        <section class="card">
          <div class="card-head">
            <h2>Audit log</h2>
            @if (auditPage(); as page) {
              <span class="dim small">{{ page.totalCount }} event(s), newest first</span>
            }
          </div>

          @if (loading()) {
            <div class="loading-row">
              <span class="spinner" aria-hidden="true"></span> Loading audit events…
            </div>
          } @else if (auditPage(); as page) {
            <div class="table-wrap" style="border: 0; border-radius: 0;">
              <table class="table">
                <thead>
                  <tr>
                    <th>Occurred</th>
                    <th>Action</th>
                    <th>Outcome</th>
                    <th>Actor</th>
                    <th>Target</th>
                    <th>Details</th>
                  </tr>
                </thead>
                <tbody>
                  @for (entry of page.items; track entry.id) {
                    <tr>
                      <td class="num">{{ formatTimestamp(entry.occurredAtUtc) }}</td>
                      <td class="mono">{{ entry.action }}</td>
                      <td>
                        <span class="badge" [class]="outcomeClass(entry)">{{
                          auditOutcomeLabel(entry.outcome)
                        }}</span>
                      </td>
                      <td>
                        <div>{{ entry.actorIdentifier || '—' }}</div>
                        <div class="dim small mono">{{ entry.actorUserId || '' }}</div>
                      </td>
                      <td>
                        <div>{{ entry.targetType }}</div>
                        <div class="dim small mono">{{ entry.targetId || '' }}</div>
                      </td>
                      <td class="cell-target small muted">{{ entry.details || '' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <div
              class="card-body"
              style="display: flex; justify-content: space-between; align-items: center; gap: 12px;"
            >
              <span class="dim small">Page {{ page.page }} of {{ page.totalPages || 1 }}</span>
              <div class="page-actions">
                <button
                  type="button"
                  class="btn btn-ghost btn-sm"
                  [disabled]="!page.hasPrevious"
                  (click)="loadAudit(page.page - 1)"
                >
                  Previous
                </button>
                <button
                  type="button"
                  class="btn btn-ghost btn-sm"
                  [disabled]="!page.hasNext"
                  (click)="loadAudit(page.page + 1)"
                >
                  Next
                </button>
              </div>
            </div>
          }
        </section>
      }
    </div>
  `,
})
export class AdminPage {
  private readonly service = inject(AdminService);

  protected readonly tab = signal<AdminTab>('users');
  protected readonly loading = signal(true);
  protected readonly problem = signal<string | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly usersPage = signal<PagedResult<UserAccount> | null>(null);
  protected readonly auditPage = signal<PagedResult<AuditEntry> | null>(null);

  protected readonly formatTimestamp = formatTimestamp;
  protected readonly auditOutcomeLabel = auditOutcomeLabel;
  protected readonly statusClass = statusClass;

  constructor() {
    void this.loadUsers(1);
  }

  protected show(tab: AdminTab): void {
    this.tab.set(tab);
    this.problem.set(null);
    this.errors.set([]);

    if (tab === 'users' && !this.usersPage()) {
      void this.loadUsers(1);
    }

    if (tab === 'audit' && !this.auditPage()) {
      void this.loadAudit(1);
    }
  }

  protected async loadUsers(page: number): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      this.usersPage.set(
        (await firstValueFrom(this.service.users(page))) as PagedResult<UserAccount>,
      );
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async loadAudit(page: number): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      this.auditPage.set(
        (await firstValueFrom(this.service.audit(page))) as PagedResult<AuditEntry>,
      );
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected outcomeClass(entry: AuditEntry): string {
    return entry.outcome === 1 ? 'badge-active' : 'badge-revoked';
  }
}
