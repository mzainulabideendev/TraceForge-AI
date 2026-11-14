# TraceForge AI

**Turn security evidence into actionable defensive intelligence.**

TraceForge AI is an open-source cybersecurity investigation platform. It connects real, evidence-backed
security findings into attack-path intelligence so engineers can understand *why* a weakness matters,
not just that it exists.

> **Authorized testing only.** TraceForge AI is built for environments you own or are explicitly
> authorized to assess. Every scan must be bound to a workspace with an approved scope.

## Status

This repository is under active, phase-gated development. The table below is the single source of truth
for what actually works today.

| Phase | Scope | Status |
|---|---|---|
| 0 | Repository baseline: build, API host, structured logging, health probes, security headers, request correlation, rate limiting, configuration validation, automated tests, CI, documentation | **Implemented and verified** |
| 1 | Backend foundation: authentication, database persistence, audit logging, API | **Implemented and verified on SQLite; PostgreSQL runtime verification BLOCKED (server absent)** |
| 2 | Workspace model, membership RBAC, authorized scopes and deterministic scope validation | **Implemented and verified on SQLite; PostgreSQL runtime verification BLOCKED (server absent)** |
| 3 | Asset discovery | Not started |
| 4 | Deterministic detection engine | Not started |
| 5 | Angular dashboard | Not started |
| 6 | Evidence-backed attack graph | Not started |
| 7 | AI investigator (provider adapters) | Not started |
| 8 | Security digital twin | Not started |
| 9 | Remediation and verification | Not started |
| 10 | Reporting | Not started |
| 11 | Incident investigation and replay | Not started |
| 12 | Honeypot integration | Not started |
| 13 | Production hardening | Not started |

**Do not assume any capability that is not listed as implemented above.** Nothing in this repository
fabricates scan results, findings, or metrics.

## Requirements

| Component | Version | Verified here |
|---|---|---|
| .NET SDK | 10.0.x | 10.0.401 |
| Node.js (future frontend) | 22 LTS or later | 24.20.0 |
| Python (future analysis service) | 3.12+ | 3.12.10 |
| PostgreSQL (Phase 1+) | 16+ | Not installed on the development machine; schema generated but server verification BLOCKED |
| Docker (optional) | 24+ | Not installed on the development machine |

The Phase 2 backend runs locally with the .NET SDK alone: the `Development` profile uses a SQLite
file database that is migrated automatically on startup. A deployed instance requires PostgreSQL.
Docker and Nmap are not required by any implemented feature.

## Quick start (Windows PowerShell)

```powershell
dotnet restore TraceForge.slnx
dotnet build TraceForge.slnx
dotnet test TraceForge.slnx
dotnet tool restore
dotnet run --project src\backend\TraceForge.Api\TraceForge.Api.csproj
```

The API listens on `http://localhost:5080` (see `Properties/launchSettings.json`). On first run in
`Development` the API creates `traceforge-dev.db`, applies migrations and seeds the `Administrator`
and `Analyst` roles.

```powershell
Invoke-RestMethod http://localhost:5080/health/live
Invoke-RestMethod http://localhost:5080/health/ready
Invoke-RestMethod http://localhost:5080/api/system/info
```

OpenAPI document (Development environment only): `http://localhost:5080/openapi/v1.json`

## API surface (Phase 2)

| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | `/health/live` | anonymous | Liveness probe; succeeds while the process serves requests |
| GET | `/health/ready` | anonymous | Readiness probe; runs real checks for log-directory writability and database connectivity |
| GET | `/api/system/info` | anonymous | Version, environment, real uptime, instance name |
| GET | `/api/auth/csrf` | anonymous | Issues the antiforgery request token and cookie |
| POST | `/api/auth/register` | anonymous + CSRF | Creates an account; the first account of a fresh installation becomes Administrator |
| POST | `/api/auth/login` | anonymous + CSRF | Verifies credentials and starts the session cookie |
| POST | `/api/auth/logout` | required + CSRF | Ends the current session |
| GET | `/api/auth/me` | required | Returns the authenticated account and roles |
| GET | `/api/admin/users` | Administrator | Paged account list |
| GET | `/api/admin/audit` | Administrator | Paged security audit log, newest first |
| POST | `/api/workspaces` | required + CSRF | Creates a workspace; caller becomes Owner |
| GET | `/api/workspaces` | required | Lists only workspaces the caller belongs to |
| GET | `/api/workspaces/{id}` | member | Workspace details; non-members receive 404 |
| PUT | `/api/workspaces/{id}` | Owner/Administrator + CSRF | Updates workspace details |
| POST | `/api/workspaces/{id}/archive` | Owner + CSRF | Archives a workspace |
| GET | `/api/workspaces/{id}/members` | member | Lists members |
| POST | `/api/workspaces/{id}/members` | Owner/Administrator + CSRF | Adds a member |
| PATCH | `/api/workspaces/{id}/members/{userId}` | Owner/Administrator + CSRF | Changes a member role |
| DELETE | `/api/workspaces/{id}/members/{userId}` | Owner/Administrator + CSRF | Removes a member |
| POST | `/api/workspaces/{id}/scopes` | member + CSRF | Registers a scope (always Pending) |
| GET | `/api/workspaces/{id}/scopes` | member | Lists scopes |
| GET | `/api/workspaces/{id}/scopes/{scopeId}` | member | Returns one scope |
| POST | `/api/workspaces/{id}/scopes/{scopeId}/approve` | Owner/Administrator + CSRF | Explicit approval with evidence |
| POST | `/api/workspaces/{id}/scopes/{scopeId}/revoke` | Owner/Administrator + CSRF | Revokes a scope |
| POST | `/api/workspaces/{id}/scopes/{scopeId}/expire` | Owner/Administrator + CSRF | Marks a scope expired after its deadline |
| PATCH | `/api/workspaces/{id}/scopes/{scopeId}/restrictions` | Owner/Administrator + CSRF | Updates restrictions; resets approval to Pending |
| POST | `/api/workspaces/{id}/scopes/evaluate` | member + CSRF | Evaluates a destination without connecting |
| GET | `/openapi/v1.json` | anonymous | OpenAPI document (Development only) |

Workspace roles are independent of the global Identity role: **Owner** manages everything including
archiving and ownership; **Administrator** updates the workspace and manages non-owner members and
scopes; **Analyst** reads and may register scopes that stay Pending. A global Administrator who is not
a workspace member receives `404` for that workspace.

Scope registration never approves a target. Approval requires an Owner or Administrator and a
non-empty evidence reference. Hostname approval does not approve the addresses it resolves to; every
resolved address must match an approved IP/CIDR scope. Private, loopback, link-local and reserved
destinations are denied unless `TraceForge:Scopes:AllowPrivateNetworks=true` **and** the matching scope
carries the `private-network` restriction.

Authentication uses an HttpOnly, SameSite=Strict session cookie (`traceforge.auth`). Every unsafe
request must send the antiforgery token from `GET /api/auth/csrf` in the `X-XSRF-TOKEN` header.
Problem responses use RFC 9457 `application/problem+json` and include `traceId`.

Rate limiting: fixed window per client IP on `/api/*` (120 requests / 60 seconds by default) with a
tighter budget on credential endpoints (10 requests / 300 seconds). Every response carries
`X-Request-Id`; problem responses include it as `traceId`.

## Configuration

Settings are validated at startup; an invalid value stops the process instead of degrading silently.

| Key | Default | Constraint |
|---|---|---|
| `TraceForge:InstanceName` | `TraceForge AI` | Non-empty, max 120 characters |
| `TraceForge:RateLimiting:PermitLimit` | `120` | 1 – 100,000 |
| `TraceForge:RateLimiting:WindowSeconds` | `60` | 1 – 3,600 |
| `TraceForge:RateLimiting:QueueLimit` | `0` | ≥ 0 |
| `TraceForge:RateLimiting:AuthPermitLimit` | `10` | 1 – 100,000 |
| `TraceForge:RateLimiting:AuthWindowSeconds` | `300` | 1 – 3,600 |
| `TraceForge:Auth:AllowSelfRegistration` | `false` | `true` only for controlled onboarding |
| `TraceForge:Auth:RequireSecureCookies` | `true` | Must stay `true` outside local HTTP development |
| `TraceForge:Auth:SessionMinutes` | `480` | 5 – 10,080 |
| `TraceForge:Auth:MaxFailedAttempts` | `5` | 3 – 20 |
| `TraceForge:Auth:LockoutMinutes` | `15` | 1 – 1,440 |
| `TraceForge:Database:Provider` | `PostgreSql` | `Sqlite` or `PostgreSql` |
| `TraceForge:Database:ConnectionString` | empty | Required; supplied per environment |
| `TraceForge:Database:ApplyMigrationsOnStartup` | `false` | `true` in the local development profile |
| `TraceForge:Scopes:AllowPrivateNetworks` | `false` | Must stay `false` outside an isolated lab; a matching scope must also carry `private-network` |

Use environment variables with double underscores in deployment, for example
`TraceForge__RateLimiting__PermitLimit=240`. See `.env.example`. Secrets are never stored in this
repository.

## Documentation

- [Architecture overview](docs/architecture/overview.md)
- [API reference](docs/api/endpoints.md)
- [Database schema](docs/database/schema.md)
- [Development setup](docs/development/setup.md)
- [Supabase PostgreSQL + MonsterASP.NET deployment](docs/deployment/supabase-monsterasp.md)
- [Testing guide](docs/development/testing.md)
- [Threat model](docs/security/threat-model.md)
- [Security policy](SECURITY.md)
- [Initial repository audit](docs/reports/initial-audit.md)
- [Phase 0 completion report](docs/reports/phase-0-baseline.md)
- [Phase 1 completion report](docs/reports/phase-1-foundation.md)
- [Phase 2 completion report](docs/reports/phase-2-workspaces.md)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Contributions that add simulated results, fabricated findings,
or unverified claims will be rejected.

## License

Apache License 2.0 — see [LICENSE](LICENSE).
