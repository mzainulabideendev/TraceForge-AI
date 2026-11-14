import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { SessionStore } from './session.store';

/**
 * Drops the client-side session view when the API reports the session is gone.
 *
 * Route guards use this signal to send the operator to the sign-in page; they never decide
 * authorization themselves.
 */
export const sessionExpiryInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionStore);

  return next(request).pipe(
    catchError((error: unknown) => {
      const isAuthEndpoint = request.url.includes('/api/auth/');
      if (error instanceof HttpErrorResponse && error.status === 401 && !isAuthEndpoint) {
        session.clear();
      }

      return throwError(() => error);
    }),
  );
};
