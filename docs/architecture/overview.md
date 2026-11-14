# Architecture Overview

Status: **Phase 1 foundation**. Sections marked *planned* describe the target design and are not yet
implemented.

## 1. Design principles

1. **Evidence before assertion.** Every finding, graph edge and AI statement must trace back to stored
   evidence with a SHA-256 digest.
2. **Deterministic core, optional intelligence.** Scanning, detection, graph construction and reporting
   must work with no AI provider configured. AI adds explanation, never authority.
3. **Scope is a hard boundary.** A target that is not inside an approved workspace scope never receives
   a connection.
4. **Modular monolith.** One deployable API with explicit module boundaries. No microservices until
   measured load justifies them.
5. **No simulated results in production paths.** Test doubles exist only inside tests.

## 2. Current state (Phase 1)

```text
+-------------------------------------------------------------+
|  TraceForge.Api  (ASP.NET Core minimal API, .NET 10)        |
|                                                             |
|  Program.cs                                                 |
|    +- Serilog structured logging (console + rolling file)    |
|    +- CorrelationIdMiddleware      -> X-Request-Id, traceId  |
|    +- SecurityHeadersMiddleware    -> CSP, nosniff, DENY ... |
|    +- ExceptionHandler + ProblemDetails (RFC 9457)           |
|    +- Cookie authentication (Identity) + antiforgery         |
|    +- RateLimiter (api + tighter auth policy)                |
|    +- Health checks (/health/live, /health/ready)            |
|    +- OpenAPI document (Development only)                    |
+------------------------------+------------------------------+
                               |
        +----------------------+----------------------+
        |                      |                      |
  TraceForge.Domain     TraceForge.Application  TraceForge.Infrastructure
  identities, audit     account + audit ports   EF Core, Identity stores,
  invariants            and DTOs                audit storage, options
                               |
        +----------------------+----------------------+
        |                                             |
  TraceForge.Migrations.Sqlite             TraceForge.Migrations.PostgreSql
  (provider-specific EF migrations)        (provider-specific EF migrations)
```

Implemented in Phase 1 and inserted at runtime: Identity accounts and roles, session cookies,
antiforgery enforcement on every unsafe `/api` request, audit records, a SQLite development profile
and a PostgreSQL deployment profile. Nothing is mocked: tests boot this exact pipeline through
`WebApplicationFactory<Program>` and use a real migrated database file.

Not implemented yet: workspaces and authorized scopes (Phase 2), assets (Phase 3), findings,
graphs, AI adapters, reporting.

## 3. Target architecture (planned)

```text
Angular dashboard (Cytoscape.js graph, evidence panels)
        |
        v
TraceForge.Api  ------------------------------------------------
        |
        +-- TraceForge.Application   use cases, DTOs, validation
        +-- TraceForge.Domain        entities, value objects, invariants
        +-- TraceForge.Infrastructure PostgreSQL (EF Core), storage, adapters
        +-- TraceForge.Security      scope validation, SSRF/DNS-rebinding guards
        +-- TraceForge.Graph         deterministic attack-graph construction
        +-- TraceForge.AI            provider abstractions + adapters
        +-- TraceForge.Reporting     HTML/PDF report generation
        |
        +-- PostgreSQL (evidence, findings, graph, audit)
        +-- Python analysis workers (selected detection modules)
        +-- Optional cloud AI providers behind IAiProvider
```

**Layering rule:** `Domain` depends on nothing. `Application` depends on `Domain`. `Infrastructure`
implements `Application` ports. `Api` composes everything; it holds no business rules.

Projects are created only when they contain real, exercised code. Phase 1 added `Domain`,
`Application`, `Infrastructure` and the two migration assemblies because each now holds code covered
by executed tests; `Security`, `Graph`, `AI` and `Reporting` are still absent.

## 4. Key decisions

| ID | Decision | Rationale | Status |
|---|---|---|---|
| ADR-001 | Modular monolith, single deployable API | 16 GB development budget; operational simplicity; boundaries enforced by project references | Accepted |
| ADR-002 | Configuration validated at startup (`ValidateOnStart`) | Misconfiguration must fail loudly, never degrade silently | Implemented |
| ADR-003 | Correlation id is the tracing key for every request and problem response | Operators must be able to join UI error, log line and audit record | Implemented |
| ADR-004 | Layer projects are added per phase, with real code | Prevents "folder theatre" that looks complete but does nothing | Implemented |
| ADR-005 | SQLite for the local development profile, PostgreSQL for deployed environments | The audited machine has neither Docker nor PostgreSQL; the product must still be runnable. EF Core provider switch keeps one model | Implemented (SQLite runtime verified; PostgreSQL runtime BLOCKED) |
| ADR-006 | Severity and confidence are separate persisted fields | A high-severity, low-confidence hypothesis must not be presented as fact | Accepted |
| ADR-007 | Stable finding fingerprint (rule id + asset + canonical evidence key) | Prevents duplicate findings across re-scans while preserving history | Accepted |
| ADR-008 | AI responses are untrusted and schema-validated; no tool execution | Prompt injection is expected, not exceptional | Accepted |

## 5. Data model (implemented portion)

Implemented in Phase 1 (EF Core `InitialCreate`, both providers):

| Table | Purpose |
|---|---|
| `AspNetUsers` | Accounts: e-mail, display name, UTC creation time, last login, lockout state |
| `AspNetRoles`, `AspNetUserRoles`, `AspNetRoleClaims`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens` | ASP.NET Core Identity schema, seeded with `Administrator` and `Analyst` |
| `AuditLogs` | Append-only security audit records: action, outcome, actor, target, UTC timestamp, IP, user agent |
| `__EFMigrationsHistory` | Applied migration ledger |

SQLite stores `DateTimeOffset` values as Unix milliseconds (integer) because the provider cannot
order by `DateTimeOffset`; PostgreSQL uses native `timestamptz`. Values are always written in UTC.

Planned (not yet created): `Workspaces`, `AuthorizedScopes`, `Assets`, `ScanJobs`, `ScanEvents`,
`Findings`, `Evidence`, `DetectionRules`, `AttackGraphNodes`, `AttackGraphEdges`, `AiInvestigations`,
`AiProviderUsage`, `RemediationTasks`, `VerificationRuns`, `Incidents`, `IncidentEvents`.

Rules: UTC timestamps, workspace-scoped queries enforced by global query filters, evidence digests,
no secrets in findings, retention policy per workspace.

## 6. Request lifecycle (implemented)

1. `CorrelationIdMiddleware` validates or generates `X-Request-Id`, sets `TraceIdentifier`, pushes the
   id into the log context.
2. `SecurityHeadersMiddleware` applies defensive headers; `no-store` on `/api`.
3. Serilog request logging records method, path, status and duration with the correlation id.
4. Rate limiter enforces the per-client fixed window on `/api/*` (tighter budget on credential
   endpoints); rejections return RFC 9457 JSON.
5. Antiforgery filter requires a valid token and cookie for every unsafe `/api` method, before the
   endpoint runs; the token is issued by `GET /api/auth/csrf`.
6. Cookie authentication resolves the session (`traceforge.auth`, HttpOnly, SameSite=Strict);
   unauthenticated requests receive RFC 9457 `401`, unauthorized roles receive `403`.
7. Endpoint executes; errors become `application/problem+json` with `traceId` and `instance`.
8. Authentication-relevant actions (registration, sign-in success/failure, lockout, logout, admin
   reads) append audit records with actor, outcome, UTC timestamp, IP and user agent.

## 7. Open architectural questions

- Analysis workers: in-process .NET for HTTP/TLS checks versus a Python service for deeper parsing.
  Decision deferred to Phase 4, based on measured needs.
- Evidence blob storage: database vs. filesystem content-addressed store. Deferred to Phase 3.
- Multi-tenancy isolation strategy (row-level filters vs. schema per workspace). Deferred to Phase 2.
