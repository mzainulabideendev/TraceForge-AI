# Threat Model (Initial)

Status: Phase 2. This model is revised at every phase; controls marked *planned* are not implemented.

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
| T-01 | SSRF: user supplies a URL that resolves to internal/metadata endpoints | Internal network access, credential theft | Workspace membership required; target normalized; private/loopback/link-local/reserved ranges denied unless lab switch and scope restriction; every resolved address must match an approved IP/CIDR scope | Implemented in policy (Phase 2); connection-time enforcement by scanners is Phase 3 |
| T-02 | DNS rebinding: hostname resolves to an in-scope IP at validation and an internal IP at connect | Scope bypass | Hostname approval never approves addresses; every resolution is re-evaluated; policy returns pinned addresses and callers must connect to the pins | Implemented policy + tests (Phase 2); connection pinning is Phase 3 |
| T-03 | Redirect escape: in-scope URL redirects to out-of-scope host | Scope bypass | Redirect must keep the same scheme, host and port and must independently pass scope validation; hop cap belongs to the scanner | Implemented policy + tests (Phase 2); scanner use is Phase 3 |
| T-04 | Command injection via Nmap arguments | Host compromise | Never build shell strings; allow-listed argument model; direct process execution with argument array | Planned (Phase 3) |
| T-05 | Prompt injection in scanned content or AI output | Policy bypass, fabricated conclusions | AI output treated as data, schema-validated, never executed; no scope or action authority; evidence references required | Accepted by design; enforcement from Phase 7 |
| T-06 | Secret leakage to cloud AI | Credential theft | Evidence redaction before transmission; explicit per-investigation consent; no credentials in prompts | Planned (Phase 7) |
| T-07 | Cross-workspace data access | Confidentiality breach | Explicit membership resolution on every workspace and scope operation; non-members receive 404; cross-workspace reads/writes tested; unique membership index | Implemented and verified (Phase 2, SQLite) |
| T-08 | Fabricated findings / false assurance | Wrong remediation priority, loss of trust | Deterministic rules with evidence digests; AI cannot create findings; verification re-execution required to close | Accepted by design; enforced from Phase 4 |
| T-09 | Unauthenticated access to API | Unauthorized scanning | Authentication on all API routes except health/system/csrf; role-based authorization; antiforgery on every unsafe request | Implemented (Phase 1) |
| T-10 | Resource exhaustion from scanning | Outage of assessed target | Conservative concurrency, rate limits, timeouts, max response size; no DoS testing | Planned (Phase 3) |
| T-11 | Audit tampering | Loss of accountability | Append-only audit records and per-scope history with actor, UTC timestamp, outcome; no update/delete API; restricted write path | Partially (Phase 2): application-level append-only, no database-level immutability |
| T-12 | Dependency compromise | Supply-chain attack | Central package pinning, dependency scanning in CI, lock files | Partially: pinned versions + `CentralPackageTransitivePinningEnabled` |
| T-13 | Privilege escalation through workspace ids or client-supplied roles | Unauthorized control of a workspace | Membership and role come only from stored rows; role parsing validates known values; Owner-only operations checked server-side; client-supplied user ids are only targets, never identity | Implemented (Phase 2) |
| T-14 | Owner lockout or self-demotion leaves a workspace unmanageable | Denial of administration | Last-owner count check inside a transaction for demote/remove; Admin cannot modify owners | Implemented (Phase 2) |
| T-15 | Silent scope widening through restriction edits | Scope bypass | Any restriction change resets approval to Pending and clears evidence; approval is a separate audited action | Implemented (Phase 2) |

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

## 6. Implemented controls (Phase 2)

- **Explicit workspace membership**: access requires a `WorkspaceMemberships` row. A global Identity
  role grants no workspace access. Every workspace and scope method resolves the caller's membership
  and role from the database before acting; no endpoint trusts a caller-supplied role or user id.
- **Least-privilege role model**: Owner (all workspace operations), Administrator (update, manage
  non-owner members and scopes), Analyst (read and register Pending scopes). Owner-only operations are
  re-checked on the server; an Administrator cannot grant, modify or remove an Owner.
- **Existence hiding**: a missing workspace and a workspace without membership both return `404`, so
  workspace ids cannot be enumerated. Members lacking a role receive `403`; the distinction is
  deliberate and covered by negative tests.
- **Last-owner protection**: demoting or removing the last Owner is rejected (`409`) using an owner
  count inside the mutation transaction; an Administrator cannot modify owners at all.
- **Archived workspaces**: archived workspaces still allow reads but reject every mutation (`409`).
- **Scope registration is never approval**: a newly registered scope is always `Pending`. Approval is a
  separate action restricted to Owner/Administrator and requires a non-empty evidence reference
  (3–500 characters). Revoked scopes cannot be re-approved.
- **Restriction changes reset approval**: editing restrictions returns the scope to `Pending`, clears
  the approval reference, approver and approval time, and requires a fresh approval. This prevents
  silent scope widening.
- **Deterministic scope normalization**: IPv4/IPv6/CIDR/Hostname/Origin targets are canonicalized before
  persistence. `/0` prefixes, IPv4-mapped IPv6, zone indices, userinfo, paths, queries, fragments,
  single-label hostnames and non-http(s) origins are rejected.
- **Hostname approval never authorizes addresses**: validation resolves the hostname and requires every
  resolved address to match an approved IP/CIDR scope; the policy returns the validated addresses as
  **pinned addresses**, and callers must connect to those pins (defeats DNS rebinding at the policy
  boundary).
- **Private-network gating**: loopback, link-local, private, CGNAT, multicast and reserved destinations
  are denied unless `TraceForge:Scopes:AllowPrivateNetworks=true` **and** the matching scope carries the
  `private-network` restriction. Either condition alone is insufficient.
- **Redirect containment**: a redirect must keep the same scheme, host and port as the approved origin
  (`RedirectOutsideScope` otherwise), then independently pass scope validation.
- **Per-scope audit history**: registration, approval, revocation, expiration and restriction changes
  append both a global `AuditLogs` record and an immutable `ScopeAuditEvents` row.

## 7. Residual risks (Phase 1 and 2)

- **Bootstrap race across instances**: the "first account becomes Administrator" check is serialized
  by a process-level lock. Two API instances starting simultaneously against one empty database can
  both observe an empty user table. A database-level guard is required before horizontal scaling.
- **Audit write failures do not block the action**: a failed audit write is logged and the
  authentication action still completes. This favours availability; alerting on that log is required.
- **No database-level immutability**: audit rows have no update/delete endpoint, but a database
  operator can still modify them. Hash chaining or a restricted database role is planned.
- **No MFA, password reset or email verification**: not implemented yet; accounts are created by an
  administrator or controlled self-registration.
- **Rate limiting remains per-process and keyed on the immediate peer address**; behind a proxy this
  needs forwarded-header configuration with a trusted-proxy allow-list (Phase 13).
- **PostgreSQL path unverified**: migrations were generated and scripted but never executed against a
  real server on this machine.
- **Scope validation is a policy boundary, not a connection guard**: the evaluator returns pinned
  addresses and denies out-of-scope targets, but no implemented component opens network connections yet.
  Phase 3 scanners are responsible for connecting only to the returned pins and never re-resolving. Until
  then, T-01/T-02/T-03 are enforced only for callers that honour the policy result.
- **DNS is trusted for resolution timing**: validation resolves the hostname once per evaluation through
  `IDnsResolver`; an attacker controlling DNS can change answers between evaluations. Pinning addresses
  in the returned decision bounds the impact, but callers must not accept a fresh resolution.
- **Audit-failure visibility**: like Phase 1, a failed audit write for a workspace or scope action is
  logged and does not roll back the action. Alerting on `Failed to write ... audit record` is required.
- **Application-level workspace isolation only**: isolation is enforced by explicit service checks, not
  database row-level security. A defect in a future query path that bypasses the access resolver would
  not be caught by the database. Database-enforced isolation remains a Phase 13 hardening option.
- **No workspace deletion or ownership transfer API**: workspaces are archived, not deleted; ownership
  transfer happens by adding another Owner and leaving. This is intentional but limits recovery when the
  only Owner is unavailable.

## 8. Out of scope

- Physical access, host-level compromise of the operator's machine, and supply-chain attacks on the
  .NET SDK itself.
- Any assessment of systems without documented authorization.
