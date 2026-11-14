# Threat Model (Initial)

Status: Phase 1. This model is revised at every phase; controls marked *planned* are not implemented.

## 1. Assets

| Asset | Why it matters |
|---|---|
| Scan findings and evidence | Contain sensitive details about the assessed environment |
| Authorized scopes | Define the legal boundary of the product's actions |
| AI provider credentials | Grant billable, external access |
| User credentials and sessions | Grant control over workspaces and scans |
| Audit logs | Provide accountability for offensive-adjacent actions |
| Assessed customer infrastructure | The product must never cause harm outside scope |

## 2. Trust boundaries

```text
Browser  --untrusted-->  API  --untrusted-->  Target systems
                          |
                          +--untrusted-->  AI providers
                          |
                          +--untrusted-->  Analysis workers / Nmap process
```

Everything crossing an arrow is untrusted input, including target responses and AI output.

## 3. Threats and controls

| ID | Threat | Impact | Controls | Status |
|---|---|---|---|---|
| T-01 | SSRF: user supplies a URL that resolves to internal/metadata endpoints | Internal network access, credential theft | Resolve then validate every address against approved scope; re-validate after DNS and on each redirect; block link-local/metadata ranges | Planned (Phase 2/3) |
| T-02 | DNS rebinding: hostname resolves to an in-scope IP at validation and an internal IP at connect | Scope bypass | Pin and re-check resolved address at connect time; no proxy, no connection reuse across resolutions | Planned (Phase 3) |
| T-03 | Redirect escape: in-scope URL redirects to out-of-scope host | Scope bypass | Validate each redirect hop before following; cap hop count | Planned (Phase 3) |
| T-04 | Command injection via Nmap arguments | Host compromise | Never build shell strings; allow-listed argument model; direct process execution with argument array | Planned (Phase 3) |
| T-05 | Prompt injection in scanned content or AI output | Policy bypass, fabricated conclusions | AI output treated as data, schema-validated, never executed; no scope or action authority; evidence references required | Accepted by design; enforcement from Phase 7 |
| T-06 | Secret leakage to cloud AI | Credential theft | Evidence redaction before transmission; explicit per-investigation consent; no credentials in prompts | Planned (Phase 7) |
| T-07 | Cross-workspace data access | Confidentiality breach | Workspace-scoped authorization on every query; global query filters; integration tests for isolation | Planned (Phase 2) |
| T-08 | Fabricated findings / false assurance | Wrong remediation priority, loss of trust | Deterministic rules with evidence digests; AI cannot create findings; verification re-execution required to close | Accepted by design; enforced from Phase 4 |
| T-09 | Unauthenticated access to API | Unauthorized scanning | Authentication on all API routes except health/system/csrf; role-based authorization for administration; antiforgery on every unsafe request | Implemented (Phase 1) |
| T-10 | Resource exhaustion from scanning | Outage of assessed target | Conservative concurrency, rate limits, timeouts, max response size; no DoS testing | Planned (Phase 3) |
| T-11 | Audit tampering | Loss of accountability | Append-only audit records with actor, UTC timestamp, outcome; restricted write path | Partially (Phase 1): append-only application path, no update/delete API; database-level immutability still planned |
| T-12 | Dependency compromise | Supply-chain attack | Central package pinning, dependency scanning in CI, lock files | Partially: pinned versions + `CentralPackageTransitivePinningEnabled` |

## 4. Implemented controls (Phase 0)

- Defensive response headers on every response: `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, restrictive `Content-Security-Policy`,
  `Permissions-Policy`, `Cross-Origin-Opener-Policy`, `Cross-Origin-Resource-Policy`,
  `Cache-Control: no-store` for `/api`, HSTS only when the request is HTTPS.
- Kestrel `Server` header suppressed (no version disclosure).
- Request correlation with strict input validation of caller-supplied ids (no header injection).
- Per-client fixed-window rate limiting on `/api` with RFC 9457 rejections.
- Startup configuration validation; invalid configuration prevents the process from serving.
- Central package management with transitive pinning; no secrets in the repository.
- RFC 9457 problem responses that never include stack traces or internal paths.

## 5. Implemented controls (Phase 1)

- **Session authentication**: ASP.NET Core Identity with an HttpOnly, SameSite=Strict session cookie
  (`traceforge.auth`), configurable `Secure` policy, sliding expiration with a configurable lifetime.
- **Antiforgery**: double-submit token (`X-XSRF-TOKEN` header + `XSRF-TOKEN` cookie) enforced by an
  endpoint filter on every unsafe `/api` method, including authenticated requests.
- **Password policy**: minimum 12 characters with digit, upper- and lowercase requirements, unique
  characters, and no forced punctuation. Lockout after 5 failed attempts for 15 minutes by default.
- **Account-enumeration resistance**: unknown-account sign-ins perform a dummy password hash and
  return the same 401 as a wrong password.
- **Role-based authorization**: `Administrator` and `Analyst` roles; administration endpoints require
  Administrator; the first account of a fresh installation becomes Administrator.
- **Credential-endpoint throttling**: a separate, tighter rate-limit policy for register/login.
- **Audit trail**: append-only records for registration, sign-in success/failure/lockout, logout and
  administrative reads, with actor, outcome, UTC timestamp, IP address and user agent.
- **UTC persistence**: `DateTimeOffset` values are stored as UTC; SQLite uses Unix milliseconds,
  PostgreSQL uses `timestamptz`.
- **Database provider separation**: distinct migration assemblies prevent SQLite and PostgreSQL
  schema drift.

## 6. Residual risks (Phase 1)

- **Bootstrap race across instances**: the "first account becomes Administrator" check is serialized
  by a process-level lock. Two API instances starting simultaneously against one empty database can
  both observe an empty user table. A database-level guard is required before horizontal scaling.
- **Audit write failures do not block the action**: a failed audit write is logged and the
  authentication action still completes. This favours availability; alerting on that log is required.
- **No database-level immutability**: audit rows have no update/delete endpoint, but a database
  operator can still modify them. Hash chaining or a restricted database role is planned.
- **No MFA, password reset or email verification**: not implemented yet; accounts are created by an
  administrator or controlled self-registration.
- **No workspace isolation yet**: all authenticated Analysts can see system metadata; workspace
  scoping arrives in Phase 2.
- **Rate limiting remains per-process and keyed on the immediate peer address**; behind a proxy this
  needs forwarded-header configuration with a trusted-proxy allow-list (Phase 13).
- **PostgreSQL path unverified**: migrations were generated and scripted but never executed against a
  real server on this machine.

## 7. Out of scope

- Physical access, host-level compromise of the operator's machine, and supply-chain attacks on the
  .NET SDK itself.
- Any assessment of systems without documented authorization.
