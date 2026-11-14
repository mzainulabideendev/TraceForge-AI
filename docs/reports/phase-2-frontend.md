# Phase 2 — Web console and deployment readiness

**Status: implemented and verified locally. Deployment to a hosting provider: NOT RUN.**
**Feature commit: `5e88921`** (`feat(web): add Angular workspace console and same-origin SPA hosting`).
**Report commit: `ffa5c93`.**

## Objective

The Phase 2 API had no user interface, and the repository had no answer for how the backend and a
console could be hosted for free. This change delivers:

1. a complete Angular console for every implemented Phase 1–2 capability;
2. same-origin hosting of that console by the API itself, which the cookie policy requires;
3. a researched, honest guide to free hosting options for the .NET backend.

It does **not** add any Phase 3 capability (no discovery, no scanning, no network requests to
assessment targets). The console only calls the existing API.

## What the console does

| Area | Screens and behaviour |
|---|---|
| Authentication | Sign in, self-registration (when the API allows it), session restore on load, sign out, 401 → sign-in redirect with `returnUrl` (validated as a same-origin path). |
| Workspaces | List of workspaces the caller is a member of, create workspace, overview with editable name/description, lifecycle timestamps, archive (Owner only, confirmed), permission summary for the caller's role. |
| Membership | Member list with roles, add member by e-mail, change role with an explicit Apply step, remove with confirmation, "you" marker, last-owner and admin-vs-owner rules surfaced when the API refuses. |
| Authorized scopes | Register (always Pending), status filters with counts, text filter, approve with a required authorization reference, revoke with optional reason, expire when past the deadline, edit restrictions (which resets approval to Pending), usable indicator. |
| Evaluation | Destination/scheme/port form, decision card with allowed/denied, reason code with an explanation, matched scope, pinned addresses, and a prefill list of currently usable scopes. Clearly states that no network connection is made. |
| Administration | Accounts and audit log tabs for global Administrators, paged, with outcome badges; a 403 from the API is rendered, not hidden. |

UI-only permission mirrors exist in `workspace-context.ts` so the interface does not offer actions that
are guaranteed to fail. They are **not** an authorization boundary; every request is re-authorized by the
API from stored membership.

## API changes required for same-origin hosting

| Change | File | Why |
|---|---|---|
| Optional static hosting of `wwwroot` and a SPA route fallback | `src/backend/TraceForge.Api/Program.cs` | The console must be same-origin: the auth and antiforgery cookies are `SameSite=Strict`, so a cross-site SPA would authenticate nothing. `/api`, `/health` and file requests still return JSON 404. |
| Path-aware Content-Security-Policy | `src/backend/TraceForge.Api/Middleware/SecurityHeadersMiddleware.cs` | JSON responses stay `default-src 'none'`. Document responses allow same-origin scripts and inline styles only, so the console can run without `unsafe-inline` or `unsafe-eval` for scripts. |
| `.gitignore` for the generated `wwwroot`, the local Supabase CLI scratch workspace and the credential file | `.gitignore` | Build output and secrets must never be committed. |

No API endpoint, authorization rule, database entity or migration was changed. The backend test suite was
re-run unchanged.

## Two defects found and fixed during verification

1. **Antiforgery token is identity-bound.** After `register`/`login`, the token cached while anonymous is
   no longer valid, so the first mutating request failed with `400 Antiforgery validation failed`. Fixed
   properly: `AuthService` resets the token after every authentication transition, and the interceptor
   keeps a one-shot refresh-and-retry as a safety net.
2. **Angular critical-CSS inlining broke under the strict CSP.** The production build injects an inline
   script that flips a `media="print"` stylesheet to `all`; `script-src 'self'` correctly blocked it, and
   the console rendered unstyled. Fixed by disabling `optimization.styles.inlineCritical` rather than
   weakening the CSP (inline scripts stay banned).

Both were caught by the browser-level check, not by the build.

## Verification

| Check | Command | Result |
|---|---|---|
| Backend build | `dotnet build TraceForge.slnx -c Release --no-incremental` | **PASS** — 0 warnings, 0 errors |
| Backend formatting | `dotnet format TraceForge.slnx --verify-no-changes` | **PASS** |
| Backend tests | `dotnet test TraceForge.slnx -c Release` | **PASS** — 142 passed, 0 failed, 0 skipped |
| Frontend build | `npm run build` | **PASS** — production bundle generated, no budget warnings |
| Frontend formatting | `npm run format:check` | **PASS** |
| Frontend unit tests | `npx ng test --watch=false` | **PASS** — 11 passed, 0 failed |
| Live HTTP end-to-end | `docs/reports/evidence/frontend-e2e.json` | **PASS** — 32 of 32 steps |
| Real browser rendering | `docs/reports/evidence/frontend-ui-verification.json` | **PASS** — 0 page errors, 0 console errors |
| Dev-server workflow (`npm start` + proxy) | `docs/reports/evidence/frontend-dev-proxy.json` | **PASS** — 5 of 5 checks, including cookies and the antiforgery header across the proxy |
| Live deployment to MonsterASP.NET free plan | `docs/reports/evidence/deployment-monsterasp.json` | **PASS** for the deployed files: health probes, SPA document, bundle, deep link, API identity and 401 behaviour. HTTPS and PostgreSQL: **NOT RUN** (see below) |
| PostgreSQL runtime | — | **NOT RUN** — no PostgreSQL server in this environment |
| Deployment to any host | — | **NOT RUN** — no host was provisioned |

### End-to-end coverage (32 steps, all PASS)

Static hosting and contract: SPA document, hashed bundle asset, client-route deep link, restrictive CSP on
JSON, JSON 404 for unknown `/api` paths, 401 for anonymous access.

Authentication and workspaces: registration, `/me`, create, list, detail, update.

Scopes: register IPv4 and hostname (both Pending), malformed target rejected with 400, evaluation before
approval denied (`ScopePending`), approval with evidence, approved IP allowed with pinned address,
outside-scope denied, unregistered private denied, **approved** private target still denied
(`PrivateNetworkBlocked`) — proving the block is policy rather than scope absence.

Authorization: analyst added and can read, analyst approval denied with 403, outsider receives 404 for both
the workspace and its scopes, permission audit records the approval, archive succeeds, mutation after
archive refused with 409.

### Evidence

| File | Contents |
|---|---|
| `docs/reports/evidence/frontend-e2e.json` | 32-step live HTTP verification with statuses and details |
| `docs/reports/evidence/frontend-ui-verification.json` | Headless-Chrome report: headings rendered, counts, zero errors |
| `docs/reports/evidence/frontend-dev-proxy.json` | Development workflow check through the Angular dev-server proxy |
| `docs/reports/evidence/frontend-unit-tests.log` | Vitest run |
| `docs/reports/evidence/phase-2-frontend.trx` | Backend test results (142 passed) |
| `docs/reports/evidence/frontend-login.png`, `frontend-register.png` | Public screens |
| `docs/reports/evidence/frontend-workspaces.png`, `frontend-scopes.png`, `frontend-evaluate.png` | Authenticated screens |

The UI verification drives a real Chrome over the DevTools protocol: it registers an account through the
page's own origin, seeds a workspace with three scopes, navigates the client-side routes and captures each
screen. Counts asserted: 1 workspace card, 3 scope rows, active tab "Authorized scopes", heading "Evaluate
a destination".

## Free hosting answer (summary)

### Live deployment

A first deployment is running at **`http://traceforge-ai.runasp.net`** (MonsterASP.NET free plan, IIS site
`site107722`), uploaded with Web Deploy using the site's publish profile. The API and the console are served
from one origin, as designed.

| Check | Result |
|---|---|
| `/health/live`, `/health/ready` | **PASS** — database and log-directory checks healthy |
| Console document, hashed bundle, client-route deep link | **PASS** |
| `/api/system/info` reports `Production` | **PASS** |
| Anonymous `/api/workspaces` returns 401; CSRF token issued | **PASS** |
| HTTPS | **NOT RUN** — not yet activated on the free plan |
| PostgreSQL | **NOT RUN** — deployed with an interim SQLite database |

Interim deployment settings, all applied through `web.config` environment variables generated in the
publish output (never committed): SQLite in `App_Data/`, Data Protection keys in `../private/keys`, and
`RequireSecureCookies=false` because the site is still HTTP-only.

**Three follow-ups are required before this is a real deployment:**

1. Ask MonsterASP support to activate HTTPS, then set `RequireSecureCookies=true`.
2. Replace SQLite with PostgreSQL. The Supabase endpoint (`db.<ref>.supabase.co`) is IPv6-only and the host
   is IPv4, so either use the Supabase **session pooler** connection string or the PostgreSQL database
   included with the hosting plan. Set `ApplyMigrationsOnStartup=true` for that first start.
3. Register the first account on the site (it becomes the global Administrator), then set
   `AllowSelfRegistration=false`.

Full detail and caveats: [`docs/deployment/free-hosting.md`](../deployment/free-hosting.md).

| Option | Free allowance | Fit |
|---|---|---|
| MonsterASP.NET free | 1 site, 5 GB, 256 MB RAM, .NET 10/11, HTTPS subdomain | **Recommended** — runs the API and serves the console from one origin; free plan is for non-commercial use |
| Azure App Service F1 | 60 CPU-min/day, no AlwaysOn, no custom-domain TLS | Works, but the app sleeps when idle |
| Azure Container Apps | 180k vCPU-s / 360k GiB-s / 2M requests per month | Needs a container; scales to zero |
| Render free | 0.1 CPU, 512 MB, idles after ~15 min | Needs PostgreSQL; no persistent disk |
| Oracle Cloud Always Free VM | Always-on VM | Most capacity, most operations |
| Static host + separate API | Generous | **Not viable as-is**: cross-site cookies are blocked with `SameSite=Strict` |

Database: the existing Supabase free PostgreSQL project (500 MB, pauses after a week of inactivity) or Neon
free (0.5 GB, no expiry).

## Known limitations

1. **Deployment is documented, not executed.** No free host was provisioned; the same-origin hosting was
   verified locally on `http://127.0.0.1:5099`.
2. **PostgreSQL runtime remains unverified** in this environment, as recorded in the Phase 2 report. The
   console makes no assumption about the database provider.
3. **UI tests are contract-level, not a browser test suite.** The Chrome check is a one-shot script driven
   over DevTools, not a committed component/e2e suite; `src/frontend` contains 11 unit tests for the
   label/format helpers only.
4. **No pagination on members or scopes.** The API returns full collections for a workspace; the console
   renders them directly. This is fine at current scale and should be revisited with pagination.
5. **`window.confirm` for destructive actions.** Archiving and member removal use the browser dialog.
   A styled confirmation component would be better; behaviour is unaffected.
6. **Free-tier assumptions are time-sensitive.** Every provider limit in the hosting guide must be
   re-checked before relying on it.
7. **The console has no offline or optimistic-update behaviour.** Every action waits for the API, which is
   the intended default-deny posture.

## Security notes

- No secret is committed. The Supabase connection file, the local Supabase CLI workspace and agent-skill
  scaffolding are ignored by `.gitignore`; the staged commit contained no `.env`, credential or
  `node_modules` path (verified before committing).
- The API remains the sole authorization boundary. The console implements no "trust the client" path and
  renders every refusal.
- The CSP for console documents permits no inline script, no eval and no third-party origin.
- The database password and Supabase secret key previously pasted into chat should still be rotated; see
  the warning at the top of [`../deployment/supabase-monsterasp.md`](../deployment/supabase-monsterasp.md).

## Changed files (summary)

New: `src/frontend/**` (Angular 22 application, 47 files including lockfile and build script),
`docs/development/frontend.md`, `docs/deployment/free-hosting.md`, this report, and the six evidence files.

Modified: `README.md`, `docs/development/setup.md`, `docs/deployment/supabase-monsterasp.md`, `.gitignore`,
`src/backend/TraceForge.Api/Program.cs`,
`src/backend/TraceForge.Api/Middleware/SecurityHeadersMiddleware.cs`.

## Git state

| Item | Value |
|---|---|
| Feature commit | `5e88921` |
| Report commit | `ffa5c93` |
| Working tree | clean after the follow-up evidence/hash commit (see `git status`) |
