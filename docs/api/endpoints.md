# API Reference

Status: **Phase 2**. Every workspace-scoped operation is authorized server-side from the stored
membership; an Angular guard is never the enforcement point.

Base path: `/api`. Unsafe methods require the antiforgery header `X-XSRF-TOKEN` obtained from
`GET /api/auth/csrf`, plus the session cookie. Errors use RFC 9457 `application/problem+json`.

## Role matrix

Workspace roles are independent of the global Identity role. A global `Administrator` who is not a
member of a workspace receives `404` for it.

| Operation | Owner | Administrator | Analyst |
|---|---|---|---|
| Read workspace, members, scopes | yes | yes | yes |
| Update workspace details | yes | yes | no |
| Archive workspace | yes | no | no |
| Add/remove members, change roles | yes | yes, except owners | no |
| Grant, demote or remove an Owner | yes | no | no |
| Register a scope (stays Pending) | yes | yes | yes |
| Approve, revoke, expire a scope | yes | yes | no |
| Update scope restrictions | yes | yes | no |

Additional rules:

- The last Owner cannot be demoted or removed (`409`).
- An Administrator cannot promote anyone to Owner or modify an existing Owner (`403`).
- Archived workspaces still allow reads but reject mutations (`409`).
- A missing workspace and a workspace in which the caller has no membership both return `404`.

## Response codes

| Condition | Status |
|---|---|
| Success | `200` (or `201` on creation, `204` on removal) |
| Invalid input / malformed target / unknown enum | `400` |
| Authenticated but not a member | `404` |
| Member lacking the required role | `403` |
| Duplicate, last owner, invalid lifecycle transition, archived workspace | `409` |

## Authentication endpoints

| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | `/api/auth/csrf` | anonymous | Issues the antiforgery token and cookie |
| POST | `/api/auth/register` | anonymous + CSRF | Creates an account; first account becomes global Administrator |
| POST | `/api/auth/login` | anonymous + CSRF | Starts a session |
| POST | `/api/auth/logout` | required + CSRF | Ends the session |
| GET | `/api/auth/me` | required | Returns the current account and roles |

## Workspace endpoints

### `POST /api/workspaces`

Creates a workspace and an Owner membership for the caller.

```json
{ "name": "Acme assessment", "description": "Optional context" }
```

Returns `201` with `WorkspaceDetail`: `id`, `name`, `description`, `status`, `callerRole`,
`createdByUserId`, `memberCount`, `scopeCount`, `createdAtUtc`, `updatedAtUtc`, `archivedAtUtc`.

### `GET /api/workspaces`

Returns the workspaces where the caller has a membership, ordered by creation time. Each item contains
`id`, `name`, `description`, `status`, `role`, `createdAtUtc`, `updatedAtUtc`.

### `GET /api/workspaces/{workspaceId}`

Returns `WorkspaceDetail`. Non-members receive `404`.

### `PUT /api/workspaces/{workspaceId}`

Owner or Administrator. Body: `{ "name": "...", "description": "..." }`. Archived workspaces return
`409`.

### `POST /api/workspaces/{workspaceId}/archive`

Owner only. Idempotent; an already archived workspace returns its current detail.

### `GET /api/workspaces/{workspaceId}/members`

Lists members: `userId`, `email`, `displayName`, `role`, `addedAtUtc`, `updatedAtUtc`.

### `POST /api/workspaces/{workspaceId}/members`

Owner or Administrator. Body: `{ "email": "user@example.test", "role": "Analyst" }`. Role values:
`Owner`, `Administrator`, `Analyst`. Unknown accounts return `400`; existing members return `409`.

### `PATCH /api/workspaces/{workspaceId}/members/{userId}`

Body: `{ "role": "Administrator" }`. Subject to owner and last-owner protection.

### `DELETE /api/workspaces/{workspaceId}/members/{userId}`

Removes a membership. Returns `204`. The last Owner cannot be removed (`409`).

## Authorized-scope endpoints

Scope types: `IPv4`, `IPv6`, `Cidr`, `Hostname`, `Origin`.

Every scope contains: scope id, workspace id, type, normalized target, approval status, approval
reference, approver, approval time, valid-from, expiration, revocation time, restrictions, creation and
modification timestamps.

### `POST /api/workspaces/{workspaceId}/scopes`

Any member. **The new scope is always `Pending`.** Registration never approves a target.

```json
{
  "type": "Origin",
  "target": "https://app.example.com",
  "validFromUtc": "2026-11-14T12:00:00Z",
  "expiresAtUtc": "2027-02-12T12:00:00Z",
  "restrictions": []
}
```

`validFromUtc` defaults to now and `expiresAtUtc` to 90 days from now when omitted. Both must be UTC;
expiration must be after valid-from and in the future.

Normalization:

| Type | Stored form | Rejected |
|---|---|---|
| `IPv4` | Canonical dotted quad | IPv6, zone index, IPv4-mapped IPv6 |
| `IPv6` | Compressed lowercase form | IPv4, zone index, IPv4-mapped IPv6 |
| `Cidr` | Network address plus prefix (`192.168.1.130/24` → `192.168.1.0/24`) | `/0`, prefix out of range, family mismatch |
| `Hostname` | Lowercase ASCII/IDNA, trailing dot removed | ports, paths, user info, whitespace, single-label names, IP literals |
| `Origin` | `scheme://host:port` with an explicit port | non-http(s), user info, path, query, fragment, invalid port |

Duplicates after normalization return `409`.

### `GET /api/workspaces/{workspaceId}/scopes`

Lists scopes. `currentlyUsable` is true only when the scope is approved, inside its validity window
and not revoked or expired.

### `GET /api/workspaces/{workspaceId}/scopes/{scopeId}`

Returns one scope, or `404` if it does not belong to that workspace.

### `POST /api/workspaces/{workspaceId}/scopes/{scopeId}/approve`

Owner or Administrator. Body: `{ "approvalReference": "CHANGE-1234" }`. Evidence is required
(3–500 characters). Approval writes a `ScopeApprovals` history row and an audit record. A revoked
scope cannot be re-approved; register a new scope instead (`409`).

### `POST /api/workspaces/{workspaceId}/scopes/{scopeId}/revoke`

Owner or Administrator. Body may include an optional `reason` (recorded in audit, never treated as
secret storage). Revocation is immediate and idempotent.

### `POST /api/workspaces/{workspaceId}/scopes/{scopeId}/expire`

Owner or Administrator. Only succeeds after the stored expiration time has passed; before then it
returns `409`. Validation also treats an approved scope past its deadline as expired even if this
endpoint was never called.

### `PATCH /api/workspaces/{workspaceId}/scopes/{scopeId}/restrictions`

Body: `{ "restrictions": ["private-network"] }`. Changing restrictions resets an approved or pending
scope to `Pending`, clears approval evidence and requires a fresh approval. Unknown restrictions
return `400`.

### `POST /api/workspaces/{workspaceId}/scopes/evaluate`

Any member. Evaluates a destination against the workspace's approved scopes **without opening a
network connection**. No connection, HTTP request or scan is performed.

```json
{ "target": "https://app.example.com", "scheme": "https", "port": 443 }
```

Response:

```json
{
  "allowed": true,
  "reason": "None",
  "detail": "Hostname and 1 resolved address(es) are approved.",
  "matchedScopeId": "…",
  "pinnedAddresses": ["203.0.113.10"]
}
```

Denial reasons: `MalformedTarget`, `NoApprovedScope`, `ScopePending`, `ScopeExpired`, `ScopeRevoked`,
`SchemeOrPortNotApproved`, `PrivateNetworkBlocked`, `ResolvedAddressNotApproved`,
`DnsResolutionFailed`, `RedirectOutsideScope`.

## How scope validation works

1. Resolve the caller's workspace membership; a non-member is denied.
2. Normalize the requested target. Malformed targets are denied.
3. Match the target against the workspace's scopes by explicit type.
4. **Origin requests** require an exact scheme + host + port match against an approved origin scope,
   then the hostname and every resolved address must also be independently approved.
5. **Hostname requests** require an approved hostname scope, then DNS resolution. Every resolved
   address must match an approved IP or CIDR scope. A hostname approval never authorizes its resolved
   addresses.
6. **IP requests** must match an exact IP scope or fall inside an approved CIDR scope.
7. Private, loopback, link-local, carrier-grade NAT, multicast and reserved addresses are denied unless
   `TraceForge:Scopes:AllowPrivateNetworks=true` **and** the matching scope carries the
   `private-network` restriction.
8. Pending, expired and revoked scopes are denied with distinct reasons.
9. The policy returns the validated addresses as **pinned addresses**. Callers must connect to those
   addresses and must not re-resolve the hostname.

Redirect validation (`IScopeValidationService.EvaluateRedirectAsync`) rejects any redirect whose
scheme, host or port differs from the approved original origin (`RedirectOutsideScope`), then
independently evaluates the redirect target.

## Administration endpoints

| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | `/api/admin/users` | global Administrator | Paged account list |
| GET | `/api/admin/audit` | global Administrator | Paged audit log, newest first |
