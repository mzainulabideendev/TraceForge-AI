# Web console (Angular) — development and build

The console lives in [`src/frontend`](../../src/frontend) and is built with the Angular CLI (Angular 22,
standalone components, zoneless change detection, signals).

## Run it

```powershell
# terminal 1 — API on http://localhost:5080 (Development, SQLite, self-registration enabled)
dotnet run --project src\backend\TraceForge.Api\TraceForge.Api.csproj

# terminal 2 — console on http://localhost:4200, proxying /api and /health to the API
cd src\frontend
npm install
npm start
```

`proxy.conf.json` forwards `/api` and `/health` to `http://localhost:5080`, so the browser sees a single
origin (`localhost:4200`). That matters: the session and antiforgery cookies are `SameSite=Strict` and
would not be sent on cross-site requests.

If your API runs on another port, change `target` in `proxy.conf.json`.

## Build it

```powershell
cd src\frontend
npm run build        # production bundle in dist/traceforge-web/browser
npm run build:api    # same build, then copied into src/backend/TraceForge.Api/wwwroot
```

`npm run build:api` is the deployment build: the API detects `wwwroot/index.html` and serves the console
from its own origin, including the client-side route fallback.

## Where things live

| Path | Purpose |
|---|---|
| `src/app/core/models.ts` | Wire contract. C# enums are serialized as **numbers** by System.Text.Json, so `WorkspaceRole` is `1\|2\|3` and `ScopeType` is `1..5`; statuses are strings. |
| `src/app/core/csrf.interceptor.ts` | Fetches `/api/auth/csrf` once and attaches `X-XSRF-TOKEN` to unsafe requests; refreshes and retries once if the API rejects the token. |
| `src/app/core/auth.service.ts` | Restore/login/register/logout. Resets the cached antiforgery token after every authentication transition, because the token is bound to the identity that requested it. |
| `src/app/core/format.ts` | Value-to-label mapping. Unknown values render as unknown — the UI never guesses a role or status. |
| `src/app/shared/workspace-context.ts` | The single workspace detail load shared by the workspace tabs, plus UI-only permission mirrors. |
| `src/app/features/**` | Lazy-loaded routes: auth, workspace list, workspace tabs (overview, members, scopes, evaluate), administration. |

## Authorization model in the UI

The console hides controls a role cannot use, and that is the *only* thing the UI does about
authorization. Every request is re-authorized by the API from the stored membership. A non-member receives
`404` for a workspace they know the id of, and the console renders that as "does not exist, or your
account is not a member".

Never add a client-side check that is meant to be the security boundary. If the API would refuse an
action, the UI must also handle that refusal (every screen renders the RFC 9457 problem response).

## Content Security Policy constraint

The API sends a strict CSP. Document responses allow `script-src 'self'` — **no inline scripts**. For that
reason `angular.json` sets `optimization.styles.inlineCritical = false`: the default critical-CSS inlining
adds an inline script that the CSP correctly blocks, which silently leaves the page unstyled. Do not
re-enable it without adding a nonce or hash to the CSP.

Inline **styles** are still allowed (`style-src 'self' 'unsafe-inline'`) because Angular injects component
styles at runtime.

## Tests and formatting

```powershell
cd src\frontend
npm test              # vitest via the Angular unit-test builder, single run in CI
npm run format:check  # prettier
npm run format        # rewrite
```

The unit tests cover the label/format helpers — the places where a wrong mapping could misrepresent a
role or an approval state. End-to-end behaviour is covered by the HTTP verification described in
[`../reports/phase-2-frontend.md`](../reports/phase-2-frontend.md).

## Adding a screen

1. Add a lazy `loadComponent` route in `src/app/app.routes.ts`.
2. Keep state in signals (`signal`, `computed`); the application is zoneless, so a plain field mutation
   will not refresh the view.
3. Call the API through a service in `src/app/core/`, not `HttpClient` directly from a component.
4. Render failures with `app-problem-banner` and `describeProblem(error)` so operators see the server's
   reason instead of a generic message.
