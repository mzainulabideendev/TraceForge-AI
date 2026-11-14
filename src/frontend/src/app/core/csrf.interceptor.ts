import { HttpErrorResponse, HttpEvent, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';
import { apiUrl } from './api-config';

const ANTIFORGERY_HEADER = 'X-XSRF-TOKEN';
const CSRF_ENDPOINT = apiUrl('/auth/csrf');
const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

let cachedToken: string | null = null;
let pendingToken: Promise<string> | null = null;

/**
 * Adds the antiforgery token to every unsafe `/api` request and always sends cookies.
 *
 * The API requires the double-submit contract: the X-XSRF-TOKEN header must match the XSRF-TOKEN
 * cookie. The token is fetched once, reused, and refreshed once if the API rejects it (for example
 * after the cookie expired or the session rotated).
 */
export const csrfInterceptor: HttpInterceptorFn = (request, next) => {
  const authorized = request.clone({ withCredentials: true });

  if (!request.url.startsWith('/api') || SAFE_METHODS.has(request.method.toUpperCase())) {
    return next(authorized);
  }

  return attachToken(authorized, next, true);
};

function attachToken(
  request: HttpRequest<unknown>,
  next: Parameters<HttpInterceptorFn>[1],
  allowRetry: boolean,
): Observable<HttpEvent<unknown>> {
  return from(csrfToken()).pipe(
    switchMap((token) =>
      next(request.clone({ setHeaders: { [ANTIFORGERY_HEADER]: token }, withCredentials: true })),
    ),
    catchError((error: unknown) => {
      if (!allowRetry || !isAntiforgeryRejection(error)) {
        return throwError(() => error);
      }

      cachedToken = null;
      return attachToken(request, next, false);
    }),
  );
}

function csrfToken(): Promise<string> {
  if (cachedToken) {
    return Promise.resolve(cachedToken);
  }

  // A plain fetch keeps the token request outside the interceptor chain, which avoids a cycle.
  pendingToken ??= fetch(CSRF_ENDPOINT, {
    method: 'GET',
    credentials: 'include',
    cache: 'no-store',
    headers: { Accept: 'application/json' },
  })
    .then(async (response) => {
      if (!response.ok) {
        throw new Error(`Could not obtain an antiforgery token (HTTP ${response.status}).`);
      }

      const body = (await response.json()) as { token?: string };
      if (!body.token) {
        throw new Error('The antiforgery endpoint returned no token.');
      }

      cachedToken = body.token;
      return body.token;
    })
    .finally(() => {
      pendingToken = null;
    });

  return pendingToken;
}

function isAntiforgeryRejection(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) {
    return false;
  }

  const body = error.error as { title?: string } | null;
  return typeof body?.title === 'string' && body.title.toLowerCase().includes('antiforgery');
}

/**
 * Drops the cached token.
 *
 * ASP.NET Core binds an antiforgery token to the identity that requested it, so signing in, signing
 * up or signing out invalidates the token issued while anonymous. Callers must reset after an
 * authentication transition; the retry above is the safety net, not the primary path.
 */
export function resetCsrfToken(): void {
  cachedToken = null;
}
