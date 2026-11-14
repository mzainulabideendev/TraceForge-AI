import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="card empty" style="margin-top: 40px;">
      <strong>Page not found</strong>
      <span>That address does not match any screen in the console.</span>
      <a class="btn btn-primary" routerLink="/workspaces">Go to workspaces</a>
    </div>
  `,
})
export class NotFoundPage {}
