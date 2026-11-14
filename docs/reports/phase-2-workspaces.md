# Phase 2 Completion Report — Workspaces, RBAC and Authorized Scopes

| Field | Value |
|---|---|
| Phase | 2 — Multi-workspace isolation, workspace RBAC and authorized scopes |
| Completion status | **COMPLETE** for workspace isolation, membership RBAC, scope lifecycle and deterministic scope validation. PostgreSQL runtime verification is **BLOCKED** (no server). See §10 |
| Commit | `a0f4dd667b5641d1dec4b539dc5e48549d863ab5` (code, tests, docs and evidence) |
| Branch | `main` |
| Date (UTC) | 2026-11-14 |
| Environment | Windows 11 Pro, .NET SDK 10.0.401, EF Core 10.0.15, SQLite runtime verified, PostgreSQL/Docker/Nmap absent |
| Baseline | Phase 1 commit `549d1b3`, 49 passing tests, clean working tree |

## 1. Scope delivered

### Implemented and verified

1. **Workspace tenancy** — `Workspace` (Active/Archived, name/description invariants, creator,
   UTC timestamps), `WorkspaceMembership` (one explicit role per user per workspace, unique database
   index), and `WorkspaceRole` (`Owner`, `Administrator`, `Analyst`) with a single permission table.
2. **Centralized server-side authorization** — every workspace and scope operation resolves the
   caller's stored membership and role from the database. A global Identity role grants no workspace
   access. Missing workspace and non-membership both return `404`; a member lacking a role returns
   `403`; mutations on an archived workspace return `409`.
3. **Workspace and member APIs** — create, list accessible, read, update, archive, list members, add
   member, change role and remove member.
4. **Last-owner protection** — demoting or removing the final Owner is rejected inside the mutation
   transaction (`409`); an Administrator cannot grant, modify or remove an Owner.
5. **Authorized-scope lifecycle** — `AuthorizedScope`, `ScopeApproval` history and `ScopeAuditEvent`
   history. Registration always produces `Pending`; approval is a separate, audited Owner/Administrator
   action requiring 3–500 characters of evidence; revoke and expire are enforced; a revoked scope
   cannot be re-approved; a restriction change resets approval to `Pending`.
6. **Deterministic target normalization** — IPv4, IPv6, CIDR, Hostname and Origin, with explicit
   rejection rules (see §5).
7. **Deterministic validation engine** — `IScopeValidationService` evaluates stored scopes, resolves
   hostnames through `IDnsResolver`, requires every resolved address to match an approved IP/CIDR
   scope, blocks private/reserved destinations unless both a deployment switch and a scope restriction
   allow them, enforces redirect containment, and returns pinned addresses.
8. **EF Core migrations for both providers** — `20261114110000_Phase2Workspaces` (SQLite) and
   `20261114110011_Phase2Workspaces` (PostgreSQL), applied and verified on a fresh SQLite database.
9. **Auditing** — global `AuditLogs` records for workspace, membership and scope actions, plus
   append-only per-scope `ScopeAuditEvents`.
10. **Tests** — 93 new tests (142 total), including positive and negative coverage for isolation,
    role denial, last-owner protection, lifecycle transitions, normalization, DNS rebinding, private
    networks and redirects.

### Explicitly not implemented (Phase 3 scope)

- No asset discovery, scanning, Nmap integration, findings, graphs, AI adapters or reporting.
- No network connection is opened by any Phase 2 code path. `POST /scopes/evaluate` is a pure policy
  evaluation.
- No frontend.

## 2. Workspace role model

| Operation | Owner | Administrator | Analyst |
|---|---|---|---|
| Read workspace, members, scopes | yes | yes | yes |
| Update workspace details | yes | yes | no |
| Archive workspace | yes | no | no |
| Add/remove members, change roles | yes | yes, except owners | no |
| Grant, demote or remove an Owner | yes | no | no |
| Register a scope (always Pending) | yes | yes | yes |
| Approve/revoke/expire a scope | yes | yes | no |
| Update scope restrictions | yes | yes | no |

Enforcement points:

- `WorkspaceService.ResolveAccessAsync` and `ScopeService.ResolveAccessAsync` are the only entry points
  for access; no endpoint trusts a body-supplied role or user id as identity.
- Role parsing validates against the known enum; unknown values return `400`.
- Global EF query filters were deliberately **not** used. Isolation is explicit and covered by
  cross-workspace negative tests, instead of relying on a filter that could fail silently.

## 3. HTTP surface added

| Method | Path | Authorization |
|---|---|---|
| POST | `/api/workspaces` | authenticated + CSRF |
| GET | `/api/workspaces` | authenticated |
| GET | `/api/workspaces/{id}` | workspace member |
| PUT | `/api/workspaces/{id}` | Owner/Administrator |
| POST | `/api/workspaces/{id}/archive` | Owner |
| GET | `/api/workspaces/{id}/members` | workspace member |
| POST | `/api/workspaces/{id}/members` | Owner/Administrator |
| PATCH | `/api/workspaces/{id}/members/{userId}` | Owner/Administrator |
| DELETE | `/api/workspaces/{id}/members/{userId}` | Owner/Administrator |
| POST | `/api/workspaces/{id}/scopes` | workspace member |
| GET | `/api/workspaces/{id}/scopes` | workspace member |
| GET | `/api/workspaces/{id}/scopes/{scopeId}` | workspace member |
| POST | `/api/workspaces/{id}/scopes/{scopeId}/approve` | Owner/Administrator |
| POST | `/api/workspaces/{id}/scopes/{scopeId}/revoke` | Owner/Administrator |
| POST | `/api/workspaces/{id}/scopes/{scopeId}/expire` | Owner/Administrator |
| PATCH | `/api/workspaces/{id}/scopes/{scopeId}/restrictions` | Owner/Administrator |
| POST | `/api/workspaces/{id}/scopes/evaluate` | workspace member |

Failure mapping: `400` validation, `404` not found/non-member, `403` insufficient role, `409`
duplicate/last owner/archived/invalid lifecycle transition. All responses use RFC 9457 problem+json.

## 4. Data model

| Table | Key constraints |
|---|---|
| `Workspaces` | FK creator → `AspNetUsers` RESTRICT; indexes on status and creation time |
| `WorkspaceMemberships` | unique `(WorkspaceId, UserId)`; workspace CASCADE; user RESTRICT |
| `AuthorizedScopes` | unique `(WorkspaceId, Type, NormalizedTarget)`; indexes on `(WorkspaceId, Status)` and expiry; workspace CASCADE; actor FKs RESTRICT |
| `ScopeApprovals` | immutable approval history; scope/workspace CASCADE; approver RESTRICT |
| `ScopeAuditEvents` | append-only per-scope history; scope/workspace CASCADE; actor RESTRICT |

SQLite stores `DateTimeOffset` as Unix milliseconds (provider-conditional converter) because it cannot
order by `DateTimeOffset`; PostgreSQL uses native `timestamptz`. Both migration assemblies were
generated; only SQLite was applied.

## 5. Normalization and validation rules

Normalization (before persistence):

| Type | Stored form | Rejected |
|---|---|---|
| `IPv4` | canonical dotted quad | IPv6, zone index, IPv4-mapped IPv6 |
| `IPv6` | compressed lowercase | IPv4, zone index, IPv4-mapped IPv6 |
| `Cidr` | network + prefix (`192.168.1.130/24` → `192.168.1.0/24`) | `/0`, out-of-range prefix, family mismatch |
| `Hostname` | lowercase ASCII/IDNA, trailing dot removed | ports, paths, userinfo, whitespace, single-label names, IP literals |
| `Origin` | `scheme://host:port` with explicit port | non-http(s), userinfo, path, query, fragment, invalid port |

Validation decision order:

1. Caller must have workspace membership with `ViewScopes`; otherwise denied.
2. Target normalized; malformed targets denied (`MalformedTarget`).
3. Exact type match against approved scopes; pending/expired/revoked produce distinct reasons.
4. Origin requires an exact scheme+host+port origin scope, then hostname and every resolved address.
5. Hostname requires an approved hostname scope, DNS resolution, and **every** resolved address to
   match an approved IP or CIDR scope (`ResolvedAddressNotApproved` otherwise).
6. Address requires exact IP match or CIDR membership.
7. Private/loopback/link-local/CGNAT/multicast/reserved denied unless
   `TraceForge:Scopes:AllowPrivateNetworks=true` **and** the matching scope has `private-network`.
8. Redirects must keep the same scheme, host and port (`RedirectOutsideScope`), then re-evaluate.
9. Successful decisions return the validated addresses as **pinned addresses**.

## 6. Test evidence

Command:

```powershell
dotnet build TraceForge.slnx -c Release --no-incremental
dotnet format TraceForge.slnx --verify-no-changes
dotnet test TraceForge.slnx -c Release --no-build --logger "trx;LogFileName=phase-2.trx" --results-directory docs\reports\evidence
```

Result: **142 passed, 0 failed, 0 skipped** (TRX total 142). Executed
2026-11-14T16:21:21+05:00 → 16:21:42+05:00 (10:51:21Z → 10:51:42Z).

| Test class | Tests | Result | Focus |
|---|---|---|---|
| `ScopeNormalizationTests` | 50 | PASS | Normalization, CIDR math, origin rules, classifier, restrictions |
| `ConfigurationValidationTests` | 14 | PASS | Startup configuration (Phase 1) |
| `AuthFlowTests` | 13 | PASS | Authentication flows (Phase 1) |
| `ScopeValidationServiceTests` | 12 | PASS | CIDR/IPv6, hostname→address coupling, rebinding, DNS failure, private gating, origin, redirects |
| `ScopeApiTests` | 10 | PASS | Lifecycle, evidence/role requirement, normalization, duplicates, expiry, isolation, evaluate |
| `WorkspaceApiTests` | 10 | PASS | Owner creation, non-member 404, Analyst/Admin limits, last owner, transfer, audit |
| `ScopeDomainTests` | 7 | PASS | Pending default, approval evidence, revoked/expired, restriction reset, validity windows |
| `AuthorizationTests` | 6 | PASS | Global role authorization (Phase 1) |
| `DatabaseTests` | 4 | PASS | Persistence (Phase 1) |
| `Phase2MigrationTests` | 4 | PASS | Both migrations applied, tables queryable, unique membership index, cascade delete |
| `CorrelationIdTests` | 3 | PASS | Correlation (Phase 1) |
| `SecurityHeaderTests` | 3 | PASS | Security headers (Phase 1) |
| `RateLimitingTests` | 2 | PASS | Rate limiting (Phase 1) |
| `HealthEndpointTests` | 2 | PASS | Health probes (Phase 1) |
| `SystemInfoEndpointTests` | 2 | PASS | System info (Phase 1) |

Tests boot the real pipeline via `WebApplicationFactory<Program>` against a real, freshly migrated
SQLite file per test. DNS is stubbed only through `IDnsResolver` in policy tests; no real DNS or
network connection occurs.

## 7. Live HTTP verification

Executed against the Release build on Kestrel (`http://127.0.0.1:5199`) with a fresh SQLite database,
real cookie authentication and antiforgery tokens. Evidence:
`docs/reports/evidence/phase-2-http-verification.json`.

| Step | Status | Observed |
|---|---|---|
| ready | 200 | Kestrel started against a fresh SQLite database |
| register-owner | 201 | First account (global Administrator) |
| create-workspace | 201 | `callerRole=1` (Owner) |
| register-scope-ipv4 | 201 | `status=Pending` |
| approve-scope | 200 | `status=Approved` |
| evaluate-approved-ip | 200 | `allowed=True; pinned=8.8.8.8` |
| evaluate-cidr-member | 200 | `allowed=True` |
| evaluate-outside-scope | 200 | `allowed=False; reason=NoApprovedScope` |
| evaluate-private-blocked | 200 | `allowed=False; reason=PrivateNetworkBlocked` |
| register-analyst | 201 | Second account |
| add-analyst-member | 201 | Workspace role Analyst |
| analyst-approve-denied | 403 | Role lacks `ApproveScopes` |
| register-outsider | 201 | Third account, not a member |
| outsider-get-workspace | 404 | Non-member cannot discover the workspace |
| last-owner-remove-denied | 409 | Last owner protected |
| archive-workspace | 200 | `status=Archived` |
| update-archived-denied | 409 | Archived workspace rejects mutations |
| audit-query | 200 | `workspace.created`, `scope.approved`, `workspace.archived` present |

18 of 18 steps produced the expected status. This run is a real process, not the test host.

## 8. Verification gates

| Gate | Command | Result |
|---|---|---|
| Clean Release build | `dotnet build TraceForge.slnx -c Release --no-incremental` | **PASS** — 0 warnings, 0 errors |
| Formatting | `dotnet format TraceForge.slnx --verify-no-changes` | **PASS** — exit 0, no changes |
| Automated tests | `dotnet test TraceForge.slnx -c Release --no-build` + TRX | **PASS** — 142/142 |
| Live HTTP | `verify-phase2.ps1` against Kestrel | **PASS** — 18/18 steps |
| SQLite migration apply | fresh file via API startup | **PASS** |
| PostgreSQL migration apply | requires a real server | **BLOCKED** — no server, no Docker |
| CI workflow execution | GitHub Actions | **NOT RUN** — no git remote configured; workflow file unchanged and unexecuted |

## 9. Evidence artifacts

SHA-256 values are of the committed Git blobs (`git cat-file blob HEAD:<path>`), so they reproduce from
a fresh checkout.

| Artifact | Bytes | SHA-256 |
|---|---|---|
| `docs/reports/evidence/phase-2.trx` | 626426 | `626a4899f836346ee2a2d4c78807363df0513497497038540de845316b725621` |
| `docs/reports/evidence/phase-2-sqlite-schema.sql` | 9719 | `4141c52ee986e87ab5bdc33e1bf74fda31de9f3dfd3492aa4370652cd5e4f87a` |
| `docs/reports/evidence/phase-2-postgresql-schema.sql` | 10549 | `27400fe2e2bdb0eb74150a62e32d8551919d70823e863317e314696eee904085` |
| `docs/reports/evidence/phase-2-http-verification.json` | 2299 | `9d5b90ae207418050ca1d440097dd0e4587c0a3b346b85907a5604f014671ac3` |

The PostgreSQL script is **generated tooling output, never executed**. It proves the model translates
to the provider; it does not prove runtime behaviour.

## 10. Known limitations and BLOCKED items

1. **PostgreSQL runtime — BLOCKED.** Migrations and SQL were generated, but no PostgreSQL server or
   Docker exists on the audited machine, so they have never been applied. Do not treat the schema as
   runtime-verified.
2. **Connection-time pinning is not implemented — by design for Phase 2.** The policy returns pinned
   addresses and denies unapproved resolutions, but no component opens connections yet. Phase 3
   scanners must connect only to the returned pins and must not re-resolve.
3. **DNS resolution is single-shot per evaluation.** Re-evaluation re-resolves; the pin bounds the
   window but a caller must honour it.
4. **Application-level isolation only.** No database row-level security. A future query path that
   bypasses the access resolver would not be caught by the database.
5. **Audit immutability is application-level.** No update/delete API exists, but a database operator
   can modify audit rows.
6. **Audit write failures do not roll back the action.** They are logged at error level; alerting is
   required.
7. **No MFA, password reset or email verification.** Unchanged from Phase 1.
8. **CI not executed.** No remote is configured, so the workflow has never run on GitHub.
9. **No workspace deletion or ownership-transfer endpoint.** Archival plus adding a second Owner is
   the supported path.

## 11. Deviations from the original plan

| Planned | Actual | Reason |
|---|---|---|
| Global EF query filters for workspace isolation | Explicit access resolver in each service | Filters fail silently and hide a missing check; explicit resolution is testable and covered by negative tests |
| Approval/revocation only | Added `POST /scopes/evaluate` | Needed to exercise the validation engine over real HTTP without implementing scanning |
| Expire returns conflict when repeated | Expire is idempotent after the deadline | Avoids a spurious `409` after the state is already correct; covered by a test |

## 12. Approval request

Phase 2 is complete and verified on SQLite. Phase 3 (asset discovery and scanning) has **not** been
started and no scan, asset or connection code exists in the tree. Approval is requested before any
Phase 3 work begins.
