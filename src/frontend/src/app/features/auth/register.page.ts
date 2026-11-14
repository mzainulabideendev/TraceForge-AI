import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { describeProblem, problemDetails } from '../../core/problem';
import { ProblemBanner } from '../../shared/problem-banner';

/**
 * Self-registration. Disabled by the API in hardened deployments
 * (`TraceForge:Auth:AllowSelfRegistration=false`); the refusal is rendered verbatim.
 */
@Component({
  selector: 'app-register-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, ProblemBanner],
  template: `
    <div class="auth-shell">
      <div class="auth-panel">
        <div class="auth-brand">
          <span class="brand-mark" aria-hidden="true">TF</span>
          <div>
            <h1 class="auth-title">Create your account</h1>
            <p class="auth-sub">
              The first account of a fresh installation becomes the global Administrator.
            </p>
          </div>
        </div>

        <app-problem-banner
          heading="Registration refused"
          [message]="problem()"
          [errors]="errors()"
        />

        <form class="card card-pad stack" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label class="label" for="displayName">Display name</label>
            <input
              id="displayName"
              class="input"
              type="text"
              formControlName="displayName"
              autocomplete="name"
            />
            @if (form.controls.displayName.touched && form.controls.displayName.invalid) {
              <span class="hint">Enter the name that should appear on audit records.</span>
            }
          </div>

          <div class="field">
            <label class="label" for="email">E-mail</label>
            <input
              id="email"
              class="input"
              type="email"
              formControlName="email"
              autocomplete="username"
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
              formControlName="password"
              autocomplete="new-password"
            />
            @if (form.controls.password.touched && form.controls.password.invalid) {
              <span class="hint">Use at least 8 characters, including a digit.</span>
            }
          </div>

          <div class="field">
            <label class="label" for="confirmPassword">Confirm password</label>
            <input
              id="confirmPassword"
              class="input"
              type="password"
              formControlName="confirmPassword"
              autocomplete="new-password"
            />
            @if (passwordsDiffer()) {
              <span class="hint">The passwords do not match.</span>
            }
          </div>

          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="busy()">
              @if (busy()) {
                <span class="spinner" aria-hidden="true"></span>
              }
              Create account
            </button>
          </div>
        </form>

        <div class="auth-links">
          <span class="muted">Already registered?</span>
          <a routerLink="/login">Sign in</a>
        </div>
      </div>
    </div>
  `,
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly form = this.formBuilder.group({
    displayName: ['', [Validators.required, Validators.maxLength(120)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', [Validators.required]],
  });

  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);
  protected readonly errors = signal<string[]>([]);

  protected passwordsDiffer(): boolean {
    const { password, confirmPassword } = this.form.getRawValue();
    return confirmPassword.length > 0 && password !== confirmPassword;
  }

  protected async submit(): Promise<void> {
    this.problem.set(null);
    this.errors.set([]);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    if (this.passwordsDiffer()) {
      this.problem.set('The password confirmation does not match.');
      return;
    }

    this.busy.set(true);
    try {
      const { displayName, email, password } = this.form.getRawValue();
      await this.auth.register(email.trim(), password, displayName.trim());
      await this.router.navigate(['/workspaces']);
    } catch (error) {
      this.problem.set(describeProblem(error));
      this.errors.set(problemDetails(error));
    } finally {
      this.busy.set(false);
    }
  }
}
