import { ChangeDetectionStrategy, Component, OnDestroy, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { roleClass, roleLabel, statusClass } from '../../core/format';
import { describeProblem, problemDetails, statusOf } from '../../core/problem';
import { WorkspaceService } from '../../core/workspace.service';
import { ProblemBanner } from '../../shared/problem-banner';
import { WorkspaceContext } from '../../shared/workspace-context';

/**
 * Workspace shell: loads the detail once, publishes it through {@link WorkspaceContext}, and hosts
 * the tab views. A non-member receives 404 from the API, which is rendered as "not found or no
 * access" without disclosing whether the workspace exists.
 */
@Component({
  selector: 'app-workspace-layout-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ProblemBanner],
  template: `
    @if (loading()) {
      <div class="loading-row">
        <span class="spinner" aria-hidden="true"></span> Loading workspace…
      </div>
    } @else if (problem()) {
      <div class="stack">
        <app-problem-banner
          heading="Workspace unavailable"
          [message]="problem()"
          [errors]="errors()"
        />
        <div>
          <a class="btn btn-ghost btn-sm" routerLink="/workspaces">Back to workspaces</a>
        </div>
      </div>
    } @else if (context.detail(); as detail) {
      <div class="page-head">
        <div class="page-head-text">
          <span class="eyebrow">Workspace</span>
          <h1>{{ detail.name }}</h1>
          <div class="meta-row">
            <span class="badge" [class]="statusClass(detail.status)">{{ detail.status }}</span>
            <span class="badge" [class]="roleClass(detail.callerRole)"
              >Your role: {{ roleLabel(detail.callerRole) }}</span
            >
            <span>{{ detail.memberCount }} member(s)</span>
            <span>·</span>
            <span>{{ detail.scopeCount }} scope(s)</span>
          </div>
        </div>
        <div class="page-actions">
          <a class="btn btn-ghost btn-sm" routerLink="/workspaces">All workspaces</a>
        </div>
      </div>

      @if (context.isArchived()) {
        <div class="alert alert-info" role="status">
          <div>
            <div class="alert-title">This workspace is archived</div>
            <div>Reads are still allowed; every mutation is rejected by the API.</div>
          </div>
        </div>
      }

      <nav class="tabs" aria-label="Workspace sections">
        <a class="tab" routerLink="overview" routerLinkActive="active">Overview</a>
        <a class="tab" routerLink="members" routerLinkActive="active">Members</a>
        <a class="tab" routerLink="scopes" routerLinkActive="active">Authorized scopes</a>
        <a class="tab" routerLink="evaluate" routerLinkActive="active">Evaluate</a>
      </nav>

      <div class="stack" style="margin-top: 18px;">
        <router-outlet />
      </div>

      <p class="dim small" style="margin-top: 22px;">
        Workspace: <span class="mono">{{ detail.id }}</span> · created {{ detail.createdAtUtc }}
      </p>
    }
  `,
})
export class WorkspaceLayoutPage implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(WorkspaceService);

  protected readonly context = inject(WorkspaceContext);

  protected readonly loading = signal(true);
  protected readonly problem = signal<string | null>(null);
  protected readonly errors = signal<string[]>([]);

  protected readonly roleLabel = roleLabel;
  protected readonly roleClass = roleClass;
  protected readonly statusClass = statusClass;

  constructor() {
    this.route.paramMap.subscribe((params) => {
      const workspaceId = params.get('workspaceId');
      if (workspaceId) {
        void this.load(workspaceId);
      }
    });
  }

  ngOnDestroy(): void {
    this.context.reset();
  }

  private async load(workspaceId: string): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    this.errors.set([]);

    try {
      const detail = await firstValueFrom(this.service.get(workspaceId));
      this.context.set(workspaceId, detail);
    } catch (error) {
      this.context.reset();
      this.problem.set(
        statusOf(error) === 404
          ? 'This workspace does not exist, or your account is not a member of it.'
          : describeProblem(error),
      );
      this.errors.set(problemDetails(error));
    } finally {
      this.loading.set(false);
    }
  }
}
