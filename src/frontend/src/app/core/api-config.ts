/**
 * Base path of the TraceForge API.
 *
 * The SPA is served from the same origin as the API in every supported deployment (the API serves
 * the built assets), and `ng serve` proxies `/api` to the local API. Relative URLs therefore keep
 * session and antiforgery cookies first-party, which the `SameSite=Strict` cookie policy requires.
 */
export const API_BASE = '/api';

/** Full path for an API endpoint, e.g. `apiUrl('/workspaces')` -> `/api/workspaces`. */
export function apiUrl(path: string): string {
  return `${API_BASE}${path.startsWith('/') ? path : `/${path}`}`;
}
