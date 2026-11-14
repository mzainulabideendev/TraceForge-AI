import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { formatTimestamp, roleClass, roleLabel, statusClass } from '../../core/format';
import { WorkspaceSummary } from '../../core/models';
import { describeProblem, problemDetails } from '../../core/problem';
import { WorkspaceService } from '../../core/workspace.service';
import { ProblemBanner } from '../../shared/problem-banner';
import { ToastService } from '../../shared/toast.service';

/** Lists only the workspaces the signed-in account is a member of. */
@Component({
  selector: 'app-workspace-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, ProblemBanner],
  template: `
    <div class="page-head">
      <div class="page-head-text">
        <span class="eyebrow">Workspaces</span>
        <h1>Your workspaces</h1>
        <p class="lede">
          A workspace is the tenant boundary for scopes and later assessments. You only see
          workspaces where you hold a membership.
        </p>
      </div>
      <div class="page-actions">
        <button type="button" class="btn btn-primary" (click)="toggleCreate()">
          {{ createOpen() ? 'Cancel' : 'New workspace' }}
        </button>
      </div>
    </div>

    <div class="stack">
      <app-problem-banner
        heading="Could not load workspaces"
        [message]="problem()"
        [errors]="errors()"
      />

      @if (createOpen()) {
        <section class="card">
          <div class="card-head">
            <h2>Create workspace</h2>
            <span class="dim small">You become its Owner.</span>
          </div>
          <form class="card-body stack" [formGroup]="createForm" (ngSubmit)="create()" novalidate>
            <div class="form-grid">
              <div class="field">
                <label class="label" for="new-name">Name</label>
                <input
                  id="new-name"
                  class="input"
                  type="text"
                  formControlName="name"
                  placeholder="Acme assessment"
                />
                @if (createForm.controls.name.touched && createForm.controls.name.invalid) {
                  <span class="hint">A name of 2–100 characters is required.</span>
                }
              </div>
              <div class="field span-2">
                <label class="label" for="new-description">Description (optional)</label>
                <textarea
                  id="new-description"
                  class="textarea"
                  formControlName="description"
                  placeholder="What is this workspace for?"
                ></textarea>
              </div>
            </div>
            <div class="form-actions">
              <button type="button" class="btn btn-ghost" (click)="toggleCreate()">Cancel</button>
              <button type="submit" class="btn btn-primary" [disabled]="creating()">
                @if (creating()) {
                  <span class="spinner" aria-hidden="true"></span>
                }
                Create workspace
              </button>
            </div>
          </form>
        </section>
      }

      @if (loading()) {
        <div class="loading-row">
          <span class="spinner" aria-hidden="true"></span> Loading workspaces…
        </div>
      } @else if (workspaces().length === 0) {
        <div class="card empty">
          <strong>No workspaces yet</strong>
          <span>Create your first workspace to register authorized scopes.</span>
          <button type="button" class="btn btn-primary" (click)="toggleCreate()">
            New workspace
          </button>
        </div>
      } @else {
        <div class="grid">
          @for (workspace of workspaces(); track workspace.id) {
            <a class="card workspace-card" [routerLink]="['/workspaces', workspace.id]">
              <div class="workspace-card-top">
                <h2>{{ workspace.name }}</h2>
                <span class="badge" [class]="roleClass(workspace.role)">{{
                  roleLabel(workspace.role)
                }}</span>
              </div>
              <p class="desc">{{ workspace.description || 'No description.' }}</p>
              <div class="meta-row">
                <span class="badge" [class]="statusClass(workspace.status)">{{
                  workspace.status
                }}</span>
                <span>Updated {{ formatTimestamp(workspace.updatedAtUtc) }}</span>
              </div>
            </a>
          }
        </div>
      }
    </div>
  `,
})
export class WorkspaceListPage {
  private readonly service = inject(WorkspaceService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly workspaces = signal<WorkspaceSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly problem = signal<string | null>(null);
  protected readonly errors = signal<string[]>([]);

  protected readonly createOpen = signal(false);
  protected readonly creating = signal(false);
  protected readonly createForm = this.formBuilder.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    description: ['', [Validators.maxLength(1000)]],
  });

  protected readonly roleLabel = roleLabel;
  protected readonly roleClass = roleClass;
  protected readonly statusClass = statusClass;
  protected readonly formatTimestamp = formatTimestamp;

  constructor() {
    void this.load();
  }

  protected toggleCreate(): void {
    this.createOpen.update((open) => !open);
  }

  protected async create(): Promise<void> {
    this.problem.set(null);
    this.errors.set([]);

    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return;
    }

    this.creating.set(true);
    try {
      const { name, description } = this.createForm.getRawValue();
      const created = await firstValueFrom(
        this.service.create({
          name: name.trim(),
          description: description.trim() ? description.trim() : null,
        }),
      );

      this.toasts.success(`Workspace "${created.name}" created.`);
      this.createForm.reset({ name: '', description: '' });
      this.createOpen.set(false);
      await this.router.navigate(['/workspaces', created.id]);
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.creating.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      this.workspaces.set(await firstValueFrom(this.service.list()));
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.loading.set(false);
    }
  }
}
