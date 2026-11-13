# Phase 0 Completion Report — Repository and Baseline

| Field | Value |
|---|---|
| Phase | 0 — Repository and baseline |
| Completion status | **COMPLETE** (see gate table in §8) |
| Commit | `cf22f3b14c97233b761164ecbf08395602636440` |
| Branch | `main` |
| Date (UTC) | 2026-11-13 |
| Environment | Windows 11 Pro, .NET SDK 10.0.401, Node 24.20.0, Python 3.12.10, no Docker/PostgreSQL/Nmap |

## 1. Scope delivered

Phase 0 establishes a repository that builds, runs, is tested, is documented and is safe to extend.
No product capability beyond the platform baseline is claimed.

### Implemented and verified

1. **Solution baseline** — `TraceForge.slnx` with `TraceForge.Api` and `TraceForge.Api.Tests`, central
   package management (`Directory.Packages.props`), shared build settings (`Directory.Build.props`),
   analyzers enabled, nullable reference types enforced.
2. **HTTP host** — ASP.NET Core 10 minimal API (`src/backend/TraceForge.Api/Program.cs`) with the real
   middleware pipeline: exception handler → correlation id → security headers → request logging →
   rate limiter → endpoints.
3. **Structured logging** — Serilog console and rolling file sinks with application, version and
   correlation id enrichment.
4. **Health probes** — `/health/live` and `/health/ready`; readiness executes the
   `log-directory` check that writes and deletes a real probe file.
5. **System identity endpoint** — `/api/system/info` returns assembly version, environment, instance
   name, real start time and uptime.
6. **Request correlation** — `X-Request-Id` validated (charset/length) or generated, echoed, pushed to
   the log context, and used as `traceId` in every problem response.
7. **Defensive headers** — `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`,
   `Content-Security-Policy`, `Permissions-Policy`, COOP, CORP, `no-store` on `/api`, HSTS only on HTTPS.
8. **Server header suppression** — Kestrel `AddServerHeader = false`.
9. **Rate limiting** — per-client fixed window on `/api` with RFC 9457 rejections; health probes exempt.
10. **Configuration validation** — `TraceForgeOptions` bound and validated on start; seven invalid
    configurations tested; startup fails fast.
11. **OpenAPI** — document served at `/openapi/v1.json` in Development only.
12. **Repository hygiene** — Apache-2.0 `LICENSE`, `.gitignore`, `.gitattributes` (LF), `.editorconfig`,
    `.env.example`, `README`, `CONTRIBUTING`, `SECURITY`, threat model, architecture, setup and testing
    guides, CI workflow, and the initial audit.
13. **Test suite** — 20 tests executed against the real pipeline via `WebApplicationFactory<Program>`.

### Explicitly not implemented (not claimed)

Authentication, authorization, persistence, workspaces, scopes, asset discovery, detection rules,
attack graph, AI providers, Angular frontend, reports, remediation, incidents, honeypot. All belong to
Phases 1–13.

## 2. Commands executed (verbatim)

```powershell
dotnet restore TraceForge.slnx
dotnet build TraceForge.slnx --configuration Release          # 0 warnings, 0 errors
dotnet format TraceForge.slnx --verify-no-changes              # exit code 0
dotnet list TraceForge.slnx package --vulnerable --include-transitive   # no vulnerable packages
dotnet test TraceForge.slnx --configuration Release --logger "trx;LogFileName=phase-0.trx" `
  --results-directory docs\reports\evidence
dotnet run --project src\backend\TraceForge.Api\TraceForge.Api.csproj --no-build --urls http://localhost:5080
```

Runtime probing (PowerShell, real HTTP):

```powershell
Invoke-WebRequest http://localhost:5080/health/live
Invoke-WebRequest http://localhost:5080/health/ready
Invoke-WebRequest http://localhost:5080/api/system/info
Invoke-WebRequest http://localhost:5080/api/nope      # 404 problem+json
```

## 3. Test results (executed)

| Command | Result | Evidence |
|---|---|---|
| `dotnet test` (Release) | **PASS** — total 20, passed 20, failed 0, skipped 0, executed 20 | `docs/reports/evidence/phase-0.trx` |

| Test class | Tests | Result |
|---|---|---|
| `HealthEndpointTests` | 2 | PASS |
| `SecurityHeaderTests` | 3 | PASS |
| `CorrelationIdTests` | 3 | PASS |
| `SystemInfoEndpointTests` | 2 | PASS |
| `RateLimitingTests` | 2 | PASS |
| `ConfigurationValidationTests` | 8 | PASS |

No test was skipped. Two tests were initially failing during development (xUnit fixture constructor
rule, nested option validation not recursing, problem-details `traceId` not using the correlation id);
each was fixed in the product or test harness and re-executed until green. Nothing was muted.

## 4. Runtime verification (executed)

| Check | Observed result |
|---|---|
| Process starts | `Now listening on: http://localhost:5080`, environment Development |
| `/health/live` | 200, `{"status":"Healthy"}`, `X-Request-Id: phase0-verify-001` |
| `/health/ready` | 200, one check `log-directory` = Healthy |
| `/api/system/info` | 200, `application: TraceForge AI`, non-empty version, uptime ≥ 0 |
| Unknown route | 404 `application/problem+json` with `traceId` and `instance` |
| Structured logs | Written to console and `logs/traceforge-<date>.log` with correlation id |

Evidence: `docs/reports/evidence/runtime-verification.json`, `runtime-stdout.log`.

## 5. Security checks performed

| Check | Method | Result |
|---|---|---|
| Defensive headers present | `SecurityHeaderTests.ApiResponses_CarryDefensiveHeaders` | PASS |
| No `Server` version disclosure | `SecurityHeaderTests.ServerHeader_IsNotDisclosed` | PASS |
| No false HSTS over plain HTTP | `SecurityHeaderTests.HstsHeader_IsAbsentOnPlainHttp` | PASS |
| Correlation-id injection rejected | `CorrelationIdTests.GeneratedId_IsUsed_WhenCallerSuppliesMalformedValue` | PASS |
| Rate limiting enforced | `RateLimitingTests.ApiSurface_Returns429WithProblemDetails_AfterLimitExceeded` | PASS |
| Health probes immune to API saturation | `RateLimitingTests.HealthProbes_AreNotSubjectToApiRateLimit` | PASS |
| Invalid config cannot serve traffic | `ConfigurationValidationTests` (7 cases) | PASS |
| No secrets in repository | Manual review + `.gitignore` + `.env.example` | PASS |
| Known-vulnerable packages | `dotnet list package --vulnerable --include-transitive` | None found |
| Format/analyzer gate | `dotnet format --verify-no-changes`, Release build with analyzers | PASS (0 warnings) |

Not yet applicable: SSRF, DNS rebinding, redirect escape, authorization, audit logging — no scanning or
authenticated surface exists yet.

## 6. Artifact hashes (SHA-256)

| Artifact | SHA-256 |
|---|---|
| `src/backend/TraceForge.Api/bin/Release/net10.0/TraceForge.Api.dll` | `C2ADCA91AC727070C34EF15A55A70AB04694968C3080DCD9F47AD34D42679C83` |
| `docs/reports/evidence/phase-0.trx` | `BB81877AFE9EB8B844356E8A68E992EC40AAAAFB234E5DEB734FF29C4F3FAE7A` |

Binary hashes are build-output specific; rebuild to compare legitimate artifacts, not to detect
tampering of source.

## 7. Known limitations

1. **No authentication.** Only health and version metadata are exposed. Do not bind this build to a
   public interface. Phase 1 is a prerequisite for any network exposure.
2. **No persistence, therefore no evidence or findings.** Nothing in this phase can record an
   assessment.
3. **No scanning.** No connection is made to any target.
4. **HSTS behaviour over HTTPS is untested** — the test server runs plain HTTP; the header is only added
   when `Request.IsHttps` is true, so this path must be verified once TLS termination is configured.
5. **Rate limiting is per-process and keyed on the immediate peer address.** Behind a reverse proxy this
   requires forwarded-header handling with a trusted-proxy allow-list (Phase 13).
6. **OpenAPI is Development-only** by design; deployment profiles must document where the document is
   served, if at all.
7. **CI has not executed on GitHub** — no remote is configured in this environment. The workflow is
   committed and validated locally (same commands, clean).
8. **Docker/PostgreSQL/Nmap are absent on the audit machine**, so any future container or PostgreSQL
   claim must be recorded as BLOCKED until verified.
9. **Layer projects from the target architecture do not exist yet** — deliberate, per ADR-004: projects
   are created with their first real code, not as empty folders.
10. **No frontend** — the dashboard arrives in Phase 5.

## 8. Phase completion gates

| Gate | Status | Evidence |
|---|---|---|
| 1. Defined requirements | Met | README status table + this report |
| 2. Implemented functionality | Met | §1, §4 |
| 3. Unit tests | Met | 20 tests, all executed |
| 4. Integration tests | Met | Tests boot the real host and pipeline over HTTP |
| 5. Security tests | Met (for implemented surface) | §5 |
| 6. Actual execution evidence | Met | §3, §4, `docs/reports/evidence/` |
| 7. Updated documentation | Met | README, architecture, setup, testing, threat model, SECURITY, CONTRIBUTING |
| 8. Completion report | Met | This document + JSON |
| 9. Explicit unresolved issues | Met | §7, §9 |
| 10. Verified Git commit | Met | `cf22f3b`, working tree clean, build/tests re-run against it |

## 9. Remaining work and Phase 1 prerequisites

Phase 1 (backend foundation) must deliver, before it can be marked complete:

- Domain/Application/Infrastructure projects with real code (no empty layers).
- EF Core model plus migrations; **SQLite development profile** and PostgreSQL configuration.
- ASP.NET Core Identity with secure cookie settings (Secure, HttpOnly, SameSite) and CSRF protection
  for cookie-authenticated state changes.
- Role-based authorization and workspace isolation enforced by query filters and covered by
  integration tests proving cross-workspace access fails.
- Append-only audit log for authentication, workspace and scan actions.
- Authorization tests and audit tests executed with evidence.

**Blocking issue for later phases:** scan safety (scope enforcement, SSRF/DNS-rebinding/redirect
validation) must be complete and tested before Phase 3 may be declared done. Recorded in the risk
register as R-03.

## 10. Approval request

Phase 0 is complete and verified. Approval is requested to proceed to **Phase 1 — Backend foundation**.
No Phase 1 work has been started.
