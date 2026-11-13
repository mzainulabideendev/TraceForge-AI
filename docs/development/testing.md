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

## Current coverage (Phase 0)

`tests/unit/TraceForge.Api.Tests` — 20 tests, all executed against the real HTTP pipeline via
`WebApplicationFactory<Program>`:

| Area | Tests | What is asserted |
|---|---|---|
| Health probes | 2 | Liveness healthy; readiness runs the `log-directory` check and reports its result |
| Security headers | 3 | `nosniff`, `DENY`, `no-referrer`, `no-store`, restrictive CSP; no `Server` header; no HSTS over plain HTTP |
| Request correlation | 3 | Valid id echoed; malformed id replaced with a generated GUID; 404 problem+json references the correlation id |
| System info | 2 | Real version/uptime; `InstanceName` reflects configuration |
| Rate limiting | 2 | 429 problem+json after the limit; health probes unaffected by a saturated API budget |
| Configuration validation | 8 | Seven invalid configurations fail startup; a valid configuration starts |

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

- `tests/integration` — database, authentication, scope enforcement end to end.
- `tests/security` — SSRF, DNS rebinding, redirect escape, prompt injection, secret redaction.
- `tests/frontend` — Angular component tests against real API contracts.
- `lab/` — isolated deliberately vulnerable applications for positive/negative detection tests.

## Lab safety

Vulnerable lab targets (for example OWASP Juice Shop or WebGoat) must run in an isolated network
with no route to production systems, and their findings must be labelled as lab-derived in reports.
