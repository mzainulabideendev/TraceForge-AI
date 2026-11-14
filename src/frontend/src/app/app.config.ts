import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { AuthService } from './core/auth.service';
import { csrfInterceptor } from './core/csrf.interceptor';
import { sessionExpiryInterceptor } from './core/session-expiry.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled' }),
    ),
    provideHttpClient(withInterceptors([csrfInterceptor, sessionExpiryInterceptor])),
    // Restore the cookie session before the first route activates, so guards never redirect a
    // signed-in operator to the sign-in screen while the session check is still in flight.
    provideAppInitializer(() => inject(AuthService).restore()),
  ],
};
