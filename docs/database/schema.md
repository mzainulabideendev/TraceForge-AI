# Database Schema

Status: **Phase 2**. SQLite runtime is verified; PostgreSQL schema is generated and scripted but has
not executed against a real server (**BLOCKED**).

TraceForge uses one EF Core context and one transaction boundary. Identity, audit, workspace and scope
tables share that context. Migrations are provider-specific:

| Project | Provider | Timestamp representation |
|---|---|---|
| `TraceForge.Migrations.Sqlite` | SQLite (development, tests) | `DateTimeOffset` stored as UNIX milliseconds (`INTEGER`) |
| `TraceForge.Migrations.PostgreSql` | PostgreSQL (deployment) | native `timestamp with time zone` |

All timestamps are written in UTC. The SQLite converter is required because SQLite cannot order or
range-query `DateTimeOffset`.

## Tables

### Phase 1

| Table | Purpose |
|---|---|
| `AspNetUsers` | Identity account with display name, UTC creation time and last-login time |
| `AspNetRoles`, `AspNetUserRoles`, `AspNetRoleClaims`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens` | ASP.NET Core Identity schema |
| `AuditLogs` | Append-only security audit records |
| `__EFMigrationsHistory` | EF migration ledger |

### Phase 2

#### `Workspaces`

| Column | Notes |
|---|---|
| `Id` | PK, GUID |
| `Name` | Required, 2–100 characters |
| `Description` | Nullable, max 1000 characters |
| `Status` | `Active` or `Archived` (stored as text) |
| `CreatedByUserId` | FK → `AspNetUsers`, `ON DELETE RESTRICT` |
| `CreatedAtUtc`, `UpdatedAtUtc`, `ArchivedAtUtc` | UTC |

Indexes: `Status`, `CreatedAtUtc`.

#### `WorkspaceMemberships`

| Column | Notes |
|---|---|
| `Id` | PK, GUID |
| `WorkspaceId` | FK → `Workspaces`, `ON DELETE CASCADE` |
| `UserId` | FK → `AspNetUsers`, `ON DELETE RESTRICT` |
| `Role` | `Owner`, `Administrator`, `Analyst` (stored as text) |
| `CreatedAtUtc`, `UpdatedAtUtc` | UTC |

Indexes: unique `(WorkspaceId, UserId)`, plus `UserId`. The unique index makes duplicate or implicit
membership impossible at the database level.

#### `AuthorizedScopes`

| Column | Notes |
|---|---|
| `Id` | PK, GUID |
| `WorkspaceId` | FK → `Workspaces`, `ON DELETE CASCADE` |
| `Type` | `IPv4`, `IPv6`, `Cidr`, `Hostname`, `Origin` (text) |
| `NormalizedTarget` | Required, max 512 characters; canonical form |
| `Status` | `Pending`, `Approved`, `Revoked`, `Expired` (text) |
| `ApprovalReference` | Nullable, max 500 characters |
| `ApprovedByUserId`, `ApprovedAtUtc` | Set by explicit approval |
| `RevokedByUserId`, `RevokedAtUtc` | Set by revocation |
| `ValidFromUtc`, `ExpiresAtUtc` | UTC validity window |
| `Restrictions` | Required text, canonical comma-separated tokens |
| `CreatedByUserId` | FK → `AspNetUsers`, `ON DELETE RESTRICT` |
| `CreatedAtUtc`, `UpdatedAtUtc` | UTC |

Indexes: unique `(WorkspaceId, Type, NormalizedTarget)`, `(WorkspaceId, Status)`, `ExpiresAtUtc`.
The unique index prevents duplicate normalized scopes; it does not replace application validation.

#### `ScopeApprovals`

Immutable history of explicit approval decisions: `Id`, `WorkspaceId`, `ScopeId`, `ApprovedByUserId`,
`Reference`, `ApprovedAtUtc`. FK scope and workspace `CASCADE`; approver `RESTRICT`. Indexes on
`ScopeId` and `WorkspaceId`.

#### `ScopeAuditEvents`

Append-only per-scope history: `Id`, `WorkspaceId`, `ScopeId`, `Action`, `ActorUserId`, `Details`,
`OccurredAtUtc`. FK scope and workspace `CASCADE`; actor `RESTRICT`. Indexes on `ScopeId`,
`WorkspaceId`, `OccurredAtUtc`.

## Relationships and deletion

```text
AspNetUsers 1---* Workspaces          (RESTRICT)
Workspaces  1---* WorkspaceMemberships (CASCADE)
AspNetUsers 1---* WorkspaceMemberships (RESTRICT)
Workspaces  1---* AuthorizedScopes     (CASCADE)
AspNetUsers 1---* AuthorizedScopes     (creator/approver/revoker, RESTRICT)
AuthorizedScopes 1---* ScopeApprovals  (CASCADE)
AuthorizedScopes 1---* ScopeAuditEvents(CASCADE)
```

Deleting a workspace removes its memberships, scopes, approvals and scope history in the database.
The application exposes archival instead of deletion. Deleting an account is restricted while it is
referenced as a workspace creator or scope actor, so audit references cannot silently disappear.

## Authorization at the data layer

Workspace access is enforced in the service layer: every workspace and scope method resolves the
caller's membership from `WorkspaceMemberships` before acting. Global EF query filters are deliberately
not used, because a missing filter would fail silently; explicit resolution plus negative tests makes
the check visible. Database row-level security is a Phase 13 hardening option and is **not**
implemented.

## Migrations

- `InitialCreate` — Phase 1 Identity and audit schema.
- `Phase2Workspaces` — Phase 2 workspace, membership, scope, approval and scope-history schema.

Add a migration after a model change:

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src\backend\TraceForge.Migrations.Sqlite --output-dir Migrations
dotnet ef migrations add <Name> --project src\backend\TraceForge.Migrations.PostgreSql --output-dir Migrations
```

Generate scripts:

```powershell
dotnet ef migrations script --project src\backend\TraceForge.Migrations.Sqlite `
  --output docs\reports\evidence\phase-2-sqlite-schema.sql
dotnet ef migrations script --project src\backend\TraceForge.Migrations.PostgreSql `
  --output docs\reports\evidence\phase-2-postgresql-schema.sql
```

## Audit immutability limitation

`AuditLogs`, `ScopeApprovals` and `ScopeAuditEvents` have no update or delete path in the application.
That is an application-level guarantee only. A database operator with sufficient privileges can still
modify or delete rows. Hash chaining, a restricted database role or an append-only database feature is
required before claiming database-level immutability. This remains a known limitation.

## Verified evidence

| Artifact | Meaning |
|---|---|
| `docs/reports/evidence/phase-2-sqlite-schema.sql` | SQL emitted from the SQLite migrations |
| `docs/reports/evidence/phase-2-postgresql-schema.sql` | SQL emitted from the PostgreSQL migrations; never executed on a server |
| `docs/reports/evidence/phase-2.trx` | Tests including migration application, table queryability, unique index and cascade behaviour |
