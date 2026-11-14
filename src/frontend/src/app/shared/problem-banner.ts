import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Renders an API failure. Displays the problem title/detail plus any validation messages, so an
 * operator can see exactly why the server refused an operation.
 */
@Component({
  selector: 'app-problem-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (message()) {
      <div class="alert alert-error" role="alert">
        <div>
          <div class="alert-title">{{ heading() }}</div>
          <div>{{ message() }}</div>
          @if (errors().length > 0) {
            <ul class="alert-list">
              @for (entry of errors(); track entry) {
                <li>{{ entry }}</li>
              }
            </ul>
          }
        </div>
      </div>
    }
  `,
})
export class ProblemBanner {
  readonly heading = input('Request refused');
  readonly message = input<string | null>(null);
  readonly errors = input<string[]>([]);
}
