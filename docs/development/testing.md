# Testing Guide

## Commands

```powershell
dotnet test TraceForge.slnx                      # all tests
dotnet test TraceForge.slnx --logger "console;verbosity=detailed"
dotnet test TraceForge.slnx --collect:"XPlat Code Coverage"
```

Tests must be **executed**. A test that was not run is reported as `NOT RUN`, never as passed.

## Test result vocabulary

| Term | Meaning |
|---|---|
| PASS | Executed, assertion satisfied |
| FAIL | Executed, assertion violated |
| SKIPPED | Deliberately not executed (must state why) |
| BLOCKED | Cannot execute (missing dependency/credential) |
| NOT RUN | Planned but not executed |

## Current coverage (Phase 2)

`tests/unit/TraceForge.Api.Tests` — **142 tests, all executed** against the real HTTP pipeline via
`WebApplicationFactory<Program>`, with a real migrated SQLite database file per test. Baseline from
Phase 0: 20 tests; Phase 1: 49 tests; Phase 2 adds 93.

| Area | Tests | What is asserted |
|---|---|---|
| Health probes | 2 | Liveness healthy; readiness runs and reports both the `log-directory` and `database` checks |
| Security headers | 3 | `nosniff`, `DENY`, `no-referrer`, `no-store`, restrictive CSP; no `Server` header; no HSTS over plain HTTP |
| Request correlation | 3 | Valid id echoed; malformed id replaced with a generated GUID; 404 problem+json references the correlation id |
| System info | 2 | Real version/uptime; `InstanceName` reflects configuration |
| Rate limiting | 2 | 429 problem+json after the limit; health probes unaffected by a saturated API budget |
| Configuration validation | 14 | Thirteen invalid configurations fail startup (core, rate limiting, auth policy, database provider); a valid configuration starts |
| Authentication flows | 13 | Bootstrap Administrator, CSRF rejection, duplicate e-mail 409, weak password/invalid e-mail 400, wrong password 401 + audit, lockout 423, logout ends session, audit records written, cookie flags (HttpOnly/SameSite/Path and Secure over HTTPS), auth-specific rate limit, self-registration disabled after bootstrap |
| Authorization | 6 | 401 problem+json without a session, 403 as Analyst, 200 as Administrator, CSRF required even when authenticated, health/system stay anonymous, audit ordering newest-first |
| Persistence | 4 | Migrations applied to the real file, Identity/audit tables queryable, registration persisted with UTC timestamps, audit records append-only through the application path |
| Scope normalization | 50 | IPv4/IPv6 canonicalization, IPv4-mapped IPv6 and zone-index rejection, CIDR masking and `/0` rejection, hostname/IDNA rules, origin scheme/host/explicit-port rules, userinfo/path/query/fragment rejection, private/reserved classification, restriction canonicalization, address unmapping |
| Scope domain invariants | 7 | New scope is Pending; approval requires evidence and records the approver; revoked cannot be re-approved; expired is unusable and cannot be approved; early expiry rejected; restriction change resets approval; invalid validity windows rejected |
| Scope validation policy | 12 | CIDR membership, IPv6 exact match, hostname approval does not authorize resolved addresses, DNS rebinding to an unapproved address denied, DNS failure denied, private network requires both switch and restriction, origin needs scheme+host+port+address, redirect containment (host/port/scheme), non-member denied, revoked/expired/pending reasons |
| Workspace API | 10 | Creator becomes Owner; invalid name 400; non-member 404 on read/update/members and empty list; Analyst read-only 403; Administrator cannot archive or touch Owners; last owner cannot be demoted/removed; ownership transfer; duplicate/missing member handling; archived workspace read-only; workspace audit actions |
| Scope API | 10 | Register→approve→revoke lifecycle; evidence and role required for approval; normalization and malformed target 400; duplicate 409; expiration enforcement; early expiry 409; restriction change resets to Pending; cross-workspace isolation 404; evaluate allows approved and denies pending/out-of-scope; per-scope audit history |
| Phase 2 migrations | 4 | Both migrations applied to a fresh SQLite file; all Phase 2 tables queryable; unique membership index enforced by the database; deleting a workspace cascades memberships and scopes |

Executed evidence: `docs/reports/evidence/phase-2.trx` (TRX, 142 passed / 0 failed / 0 skipped).
Live HTTP verification against a real Kestrel process:
`docs/reports/evidence/phase-2-http-verification.json` (18 steps).

**Boundaries of this evidence:** tests run on SQLite. PostgreSQL migrations were generated and a SQL
script was exported, but no PostgreSQL server exists on this machine, so those migrations have never
been executed (**BLOCKED**).

DNS is the only external dependency replaced in tests: service-level policy tests inject a stub
`IDnsResolver` so no real name resolution or network connection occurs. The HTTP tests never resolve
hostnames to a live network.

## Rules for new tests

1. **Assert observable behaviour**, not implementation details or internal call order.
2. **No mocks of the system under test.** Use the real pipeline; use doubles only for external I/O
   that cannot run here, and label such tests clearly.
3. **Every security control needs a negative test** (for example: out-of-scope target rejected,
   malformed AI response rejected).
4. **Fixtures are fixtures.** Tests must not depend on the developer's machine state, timezone or
   network access.
5. **Deterministic.** No sleeps, no reliance on random ordering, no wall-clock equality assertions.

## Evidence recording

When a phase completes, store the raw output in `docs/reports/evidence/`:

```powershell
dotnet test TraceForge.slnx --logger "trx;LogFileName=phase-X.trx" `
  --results-directory docs\reports\evidence
```

The phase report must list the command, timestamp, pass/fail counts and the evidence file path.

## Planned test layers (later phases)

- `tests/integration` — multi-service flows. Phase 1 authentication, authorization and persistence are
  already exercised end-to-end in `tests/unit/TraceForge.Api.Tests` against a real migrated database;
  a separate project will be added only when a flow spans more than the API process.
- `tests/security` — SSRF, DNS rebinding, redirect escape, prompt injection, secret redaction.
- `tests/frontend` — Angular component tests against real API contracts.
- `lab/` — isolated deliberately vulnerable applications for positive/negative detection tests.

## Lab safety

Vulnerable lab targets (for example OWASP Juice Shop or WebGoat) must run in an isolated network
with no route to production systems, and their findings must be labelled as lab-derived in reports.
