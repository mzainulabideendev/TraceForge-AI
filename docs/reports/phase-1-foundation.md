# Phase 1 Completion Report — Backend Foundation

| Field | Value |
|---|---|
| Phase | 1 — Backend foundation |
| Completion status | **COMPLETE** for authentication, persistence, auditing and API surface. PostgreSQL runtime verification is **BLOCKED** (no server). See §8 |
| Commit | `c9c88ef` (code, tests, docs and evidence) |
| Branch | `main` |
| Date (UTC) | 2026-11-14 |
| Environment | Windows 11 Pro, .NET SDK 10.0.401, SQLite (runtime verified), PostgreSQL/Docker/Nmap absent |

## 1. Scope delivered

### Implemented and verified

1. **Layered backend with real code (ADR-004)** — `TraceForge.Domain` (audit entry, audit actions,
   outcomes, role names, display-name invariant), `TraceForge.Application` (account and audit ports,
   DTOs, paging, request-context port), `TraceForge.Infrastructure` (EF Core context, Identity stores,
   account service, audit writer/reader, options, request context), all referenced by `TraceForge.Api`.
2. **Authentication** — ASP.NET Core Identity with cookie authentication (`traceforge.auth`, HttpOnly,
   SameSite=Strict, Path=/, configurable `Secure` policy, sliding expiration). Sign-in, sign-out,
   current account. Unknown-account sign-ins perform a dummy hash so timing does not disclose account
   existence.
3. **Antiforgery** — double-submit token (`X-XSRF-TOKEN` header, `XSRF-TOKEN` cookie) enforced by an
   endpoint filter on every unsafe `/api` request, including authenticated ones. The token is issued
   by `GET /api/auth/csrf`.
4. **Authorization** — `Administrator` and `Analyst` roles; `/api/admin/*` requires Administrator.
   Unauthenticated requests receive RFC 9457 `401`, wrong role receives `403` (no login redirects).
5. **Bootstrap rule** — the first account of a fresh installation becomes Administrator even when
   self-registration is disabled; subsequent accounts require self-registration and become Analyst.
   Serialized by a process-level `SemaphoreSlim` (see limitation §7).
6. **Account policy** — password length 12 with upper/lower/digit/4 unique characters and no forced
   punctuation; lockout after 5 failed attempts for 15 minutes (both configurable).
7. **Audit log** — append-only records for registration, sign-in success/failure/lockout, logout,
   user listing and audit reads, with actor id, actor identifier, target, outcome, UTC timestamp, IP
   address and user agent.
8. **Persistence** — EF Core model for Identity plus `AuditLogs`; provider-specific migration
   assemblies; SQLite development profile and PostgreSQL deployment profile.
9. **SQLite timestamp correctness** — `DateTimeOffset` is stored as UNIX milliseconds for SQLite
   (the provider cannot `ORDER BY DateTimeOffset`); PostgreSQL uses `timestamptz`. Values are always
   UTC.
10. **Readiness** — `/health/ready` now runs a real `database` check in addition to `log-directory`.
11. **Configuration** — validated auth, database and rate-limit options (fail fast on invalid values);
    separate, tighter rate-limit policy for credential endpoints.
12. **Tests** — 49 tests executed against the real host with a real migrated SQLite database per test.
13. **Documentation** — README status/API/config, architecture, setup (migrations and provider
    profiles), testing guide and threat model updated to match the implementation.

### Explicitly not implemented (not claimed)

Workspaces and authorized-scope enforcement (Phase 2), asset discovery, detection engine, attack
graph, AI providers, Angular UI, reporting, remediation, incidents, honeypot. MFA, password reset and
email verification are also absent.

> **Scope clarification:** the Phase 0 report listed workspace isolation among Phase 1 prerequisites.
> Workspaces are the subject of Phase 2, and no workspace table, query filter or isolation test exists
> in this phase. The README status table was corrected to reflect the delivered scope. This is a
> deliberate phase-gate decision, not an implemented capability.

## 2. Commands executed (verbatim)

```powershell
dotnet restore TraceForge.slnx
dotnet build TraceForge.slnx --configuration Release --no-incremental   # 0 warnings, 0 errors
dotnet format TraceForge.slnx --verify-no-changes                       # exit code 0
dotnet list TraceForge.slnx package --vulnerable --include-transitive  # no vulnerable packages
dotnet test TraceForge.slnx --configuration Release `
  --logger "trx;LogFileName=phase-1.trx" --results-directory docs\reports\evidence

dotnet tool restore
dotnet ef migrations add InitialCreate --project src\backend\TraceForge.Migrations.Sqlite --output-dir Migrations
dotnet ef migrations add InitialCreate --project src\backend\TraceForge.Migrations.PostgreSql --output-dir Migrations
dotnet ef migrations script --project src\backend\TraceForge.Migrations.PostgreSql `
  --output docs\reports\evidence\phase-1-postgresql-schema.sql

dotnet run --project src\backend\TraceForge.Api\TraceForge.Api.csproj --no-build --urls http://localhost:5080
```

Live HTTP verification was performed with `curl.exe` against the running process (see §4).

## 3. Test results (executed)

| Command | Result | Evidence |
|---|---|---|
| `dotnet test` (Release, TRX) | **PASS** — total 49, passed 49, failed 0, skipped 0 | `docs/reports/evidence/phase-1.trx` |

| Test class | Tests | Result |
|---|---|---|
| `HealthEndpointTests` | 2 | PASS |
| `SecurityHeaderTests` | 3 | PASS |
| `CorrelationIdTests` | 3 | PASS |
| `SystemInfoEndpointTests` | 2 | PASS |
| `RateLimitingTests` | 2 | PASS |
| `ConfigurationValidationTests` | 14 | PASS |
| `AuthFlowTests` | 13 | PASS |
| `AuthorizationTests` | 6 | PASS |
| `DatabaseTests` | 4 | PASS |

No test was skipped and nothing was muted.

### Defects found by execution and fixed (not hidden)

1. **SQLite could not order audit rows** — `ORDER BY DateTimeOffset` failed at runtime with
   `SQLite Error 1`. Fixed with a provider-conditional value converter (Unix milliseconds) and
   migrations regenerated for both providers.
2. **Late configuration overrides were ignored** — `AddTraceForgeAuthentication` read
   `RequireSecureCookies` eagerly during service registration, so configuration sources added later
   (secrets, test overrides) had no effect. Cookie hardening and session lifetime now bind through
   `IOptions<AuthOptions>` post-configuration. The secure-cookie test proves the override takes effect.
3. **Role assertions were order-dependent** — a class-scoped database made only the first test see the
   bootstrap Administrator. Tests now use a fresh database per test (`ApiTestBase`).
4. **Antiforgery identity binding** — reusing a pre-authentication token after sign-in correctly
   returned 400. The test client now refreshes the token after identity changes.
5. **Secure cookies over plain HTTP** — ASP.NET Core rejects `SecurePolicy.Always` on a non-TLS
   request with 500. The secure-cookie test now runs over `https://localhost` through the test server.

## 4. Runtime verification (executed)

Live flow against a freshly migrated SQLite database, captured in
`docs/reports/evidence/phase-1-auth-verification.json`:

| Step | Status | Observed detail |
|---|---|---|
| `GET /api/auth/csrf` | 200 | Antiforgery token issued |
| `POST /api/auth/register` without token | 400 | Blocked by antiforgery |
| `POST /api/auth/register` (first account) | 201 | Role `Administrator`, signed in |
| `GET /api/auth/me` | 200 | `admin@example.test`, role `Administrator` |
| `GET /api/admin/users` as Administrator | 200 | `totalCount=1` |
| `GET /api/admin/audit` as Administrator | 200 | `totalCount=2`, newest `admin.users.listed` |
| `POST /api/auth/logout` | 204 | Session ended |
| `GET /api/auth/me` after logout | 401 | Unauthenticated |
| `GET /api/admin/users` anonymous | 401 | Rejected |
| `POST /api/auth/login` correct password | 200 | Authenticated |
| `POST /api/auth/login` wrong password | 401 | Invalid credentials |
| `POST /api/auth/register` second account | 201 | Role `Analyst` |
| `GET /api/admin/users` as Analyst | 403 | Forbidden |

Startup and readiness evidence (`docs/reports/evidence/phase-1-startup.log`):

```text
Database ready. Provider=Microsoft.EntityFrameworkCore.Sqlite PendingMigrationsApplied=1 RolesSeeded=Administrator,Analyst
/health/ready -> 200 {"status":"Healthy", checks: log-directory Healthy, database Healthy}
```

## 5. Security checks performed

| Check | Method | Result |
|---|---|---|
| Unsafe request without antiforgery token | `Register_WithoutAntiforgeryToken_IsRejected` | PASS (400) |
| Authenticated request without antiforgery token | `UnsafeRequest_WithoutAntiforgeryToken_IsRejected_EvenWhenAuthenticated` | PASS (400, session intact) |
| Session cookie flags | `AuthenticationCookie_IsHttpOnly_SameSiteStrict_AndPathScoped` | PASS |
| `Secure` flag when configured | `SessionCookie_IsMarkedSecure_WhenConfigured` (HTTPS) | PASS |
| Session ends on logout | `Logout_EndsSession_AndSubsequentRequestsAreUnauthorized` | PASS |
| RBAC denies Analyst | `AdminEndpoint_AsAnalyst_Returns403` | PASS |
| RBAC denies anonymous | `AdminEndpoint_WithoutSession_Returns401ProblemDetails` | PASS |
| Weak password / invalid e-mail rejected | `Register_WeakPassword…`, `Register_InvalidEmail…` | PASS |
| Duplicate account rejected | `Register_DuplicateEmail_ReturnsConflict` | PASS (409) |
| Lockout after repeated failures | `Login_LocksAccount_AfterRepeatedFailures` | PASS (423) |
| Credential endpoints rate limited | `LoginEndpoint_IsRateLimitedPerClient` | PASS (429) |
| Self-registration closed after bootstrap | `Register_WhenSelfRegistrationDisabled…` | PASS (403) |
| Failed sign-ins audited | `Login_WithWrongPassword_ReturnsUnauthorized_AndIsAudited` | PASS |
| Audit append-only through API | `AuditRecords_AreAppendOnly_NoUpdateOrDeletePathExists` | PASS |
| Invalid config cannot serve | `ConfigurationValidationTests` (13 invalid cases) | PASS |
| No secrets committed | Evidence logs reviewed for password/token material | PASS |
| Known-vulnerable packages | `dotnet list package --vulnerable --include-transitive` | None found |
| Format/analyzer gate | `dotnet format --verify-no-changes`, full Release rebuild | PASS (0 warnings) |

## 6. Artifact hashes (SHA-256)

| Artifact | SHA-256 |
|---|---|
| `src/backend/TraceForge.Api/bin/Release/net10.0/TraceForge.Api.dll` | `7F7FBBA8BABE7BFFA7B6712F0D6EC38304A4EB9C09B0FA14A665D6B3929D03AA` |
| `docs/reports/evidence/phase-1.trx` | `2C35351BC3EFAAAAA06A5C7D417E28747DB8492A1D0AA8B19E579BFF38B0F4AA` |
| `docs/reports/evidence/phase-1-auth-verification.json` | `D71DE3612EA3C11CB2B8D9EAD2F8D3E226CC1F4A4C5B98B7197007BFE8328C86` |
| `docs/reports/evidence/phase-1-postgresql-schema.sql` | `6732B64F85FC4821B715D678C6D18D39B5DFFDE2BD62E02696AAB151F18A99E8` |
| `docs/reports/evidence/phase-1-startup.log` | `63968E94F1C29FA3E6093289C2BC8EC339016345B83605797AED43EE7EBE7872` |
| `docs/reports/evidence/phase-1-auth.log` | `DEACE3429B6B324F85AF64F967302B0CF64A7C69BDC0506D85999FB56028EC3B` |

Binary hashes are build-output specific; rebuild to compare legitimate artifacts, not to detect
source tampering.

## 7. Known limitations

1. **PostgreSQL is unverified.** Migrations were generated and a SQL script exported, but no
   PostgreSQL server exists on this machine, so the script has never executed. This is **BLOCKED**,
   not passed.
2. **Bootstrap race across instances.** The "first account becomes Administrator" decision is
   serialized only inside one process. Multiple instances starting against one empty database can
   both observe an empty user table. A database-level guard is required before horizontal scaling.
3. **Audit write failures do not fail the action.** A failed audit insert is logged and authentication
   proceeds (availability choice). Alerting on that log is required.
4. **No database-level audit immutability.** No update/delete endpoint exists, but an operator with
   database access can modify rows. Hash chaining or a restricted role is planned.
5. **No MFA, password reset or e-mail verification.**
6. **No workspaces or scope enforcement** (Phase 2). Every Analyst currently sees only system
   metadata, but there is no workspace isolation to test yet.
7. **CI has not executed on GitHub** — no remote is configured in this environment. The same
   commands pass locally.
8. **Docker, PostgreSQL and Nmap are absent** — any container, PostgreSQL or Nmap claim must be
   recorded as BLOCKED until verified.
9. **Rate limiting is per-process and keyed on the immediate peer address**; a reverse proxy needs
   forwarded-header configuration with a trusted-proxy allow-list (Phase 13).
10. **HSTS over HTTPS remains untested** (test server runs plain HTTP).

## 8. Phase completion gates

| Gate | Status | Evidence |
|---|---|---|
| 1. Defined requirements | Met | README status table, §1, scope clarification |
| 2. Implemented functionality | Met (delivered scope) | §1, §4 |
| 3. Unit tests | Met | 49 tests, all executed |
| 4. Integration tests | Met | Real host, real HTTP pipeline, real migrated database file per test |
| 5. Security tests | Met (for implemented surface) | §5 |
| 6. Actual execution evidence | Met | §3, §4, `docs/reports/evidence/` |
| 7. Updated documentation | Met | README, architecture, setup, testing, threat model, `.env.example` |
| 8. Completion report | Met | This document + JSON |
| 9. Explicit unresolved issues | Met | §7, JSON `unresolvedIssues` |
| 10. Verified Git commit | Met | `c9c88ef`, build/tests re-run against it, tree clean before report |

## 9. Remaining work and Phase 2 prerequisites

Phase 2 (workspace model and authorized-scope enforcement) must deliver:

- Workspace and membership entities, with workspace-scoped authorization on every query.
- Global query filters plus integration tests proving cross-workspace reads and writes fail.
- Authorized scope records bound to a workspace, with validation of scope membership before any
  target interaction is scheduled.
- Audit records for workspace and scope changes.

**Blocking issue for later phases (unchanged):** scan safety (scope enforcement,
SSRF/DNS-rebinding/redirect validation) must be complete and tested before Phase 3 may be declared
done. Recorded as R-03.

**Carried blocker:** PostgreSQL execution must be verified on a machine with a server (or CI service
container) before the deployed database profile can be called verified.

## 10. Approval request

Phase 1 is complete for the delivered scope, with the PostgreSQL runtime gap explicitly BLOCKED.
Approval is requested to proceed to **Phase 2 — Workspace model and authorized-scope enforcement**.
No Phase 2 work has been started.
