import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { describeProblem, problemDetails } from '../../core/problem';
import { ProblemBanner } from '../../shared/problem-banner';

/** Credential sign-in. The API starts the session cookie; the SPA stores no token. */
@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, ProblemBanner],
  template: `
    <div class="auth-shell">
      <div class="auth-panel">
        <div class="auth-brand">
          <span class="brand-mark" aria-hidden="true">TF</span>
          <div>
            <h1 class="auth-title">TraceForge AI</h1>
            <p class="auth-sub">Sign in to the workspace console.</p>
          </div>
        </div>

        <app-problem-banner heading="Sign-in failed" [message]="problem()" [errors]="errors()" />

        <form class="card card-pad stack" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label class="label" for="email">E-mail</label>
            <input
              id="email"
              class="input"
              type="email"
              autocomplete="username"
              formControlName="email"
              placeholder="operator@example.test"
            />
            @if (form.controls.email.touched && form.controls.email.invalid) {
              <span class="hint">Enter a valid e-mail address.</span>
            }
          </div>

          <div class="field">
            <label class="label" for="password">Password</label>
            <input
              id="password"
              class="input"
              type="password"
              autocomplete="current-password"
              formControlName="password"
            />
            @if (form.controls.password.touched && form.controls.password.invalid) {
              <span class="hint">A password is required.</span>
            }
          </div>

          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="busy()">
              @if (busy()) {
                <span class="spinner" aria-hidden="true"></span>
              }
              Sign in
            </button>
          </div>
        </form>

        <div class="auth-links">
          <span class="muted">No account yet?</span>
          <a routerLink="/register">Create an account</a>
        </div>
      </div>
    </div>
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly form = this.formBuilder.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);
  protected readonly errors = signal<string[]>([]);

  protected async submit(): Promise<void> {
    this.problem.set(null);
    this.errors.set([]);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    try {
      const { email, password } = this.form.getRawValue();
      await this.auth.login(email.trim(), password);
      await this.router.navigateByUrl(this.returnUrl());
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** Only same-origin absolute paths are honoured, so `returnUrl` cannot be used as an open redirect. */
  private returnUrl(): string {
    const requested = this.route.snapshot.queryParamMap.get('returnUrl');
    if (!requested || !requested.startsWith('/') || requested.startsWith('//')) {
      return '/workspaces';
    }

    return requested;
  }
}
