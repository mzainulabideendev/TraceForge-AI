# Development Setup

## Verified environment

Audited on 2026-11-13 on the primary development machine:

| Tool | Detected | Notes |
|---|---|---|
| OS | Windows 11 Pro | PowerShell 5.1 available |
| RAM | 15.37 GB | Development profile keeps services minimal |
| .NET SDK | 10.0.401 | Required |
| Node.js / npm | v24.20.0 / 11.19.0 | Required for the Angular web console |
| Python | 3.12.10 | Only needed when Python analysis modules land |
| Git | 2.55.0 | |
| Docker | **not installed** | Optional; PostgreSQL via Docker is documented but unverified here |
| PostgreSQL | **not installed** | Required for deployed profiles; Phase 1 uses a SQLite development profile |
| Nmap | **not installed** | Optional integration; Phase 3 must work without it |

## Build and run

```powershell
git clone <repository-url>
cd "Trace Forge"

dotnet restore TraceForge.slnx
dotnet build TraceForge.slnx --configuration Release
dotnet test TraceForge.slnx

dotnet run --project src\backend\TraceForge.Api\TraceForge.Api.csproj
```

Default URL: `http://localhost:5080`.

## Web console

```powershell
cd src\frontend
npm install
npm start          # http://localhost:4200, proxies /api and /health to http://localhost:5080
```

Start the API first. The dev server's proxy keeps the console and the API on one browser origin, which
the `SameSite=Strict` session cookie requires. For a single-origin deployment build the console into the
API with `npm run build:api`; the API then serves it from `wwwroot`.

Details, including the Content Security Policy constraint that disables critical-CSS inlining, are in the
[web console guide](frontend.md).

## Verify a running instance

```powershell
Invoke-RestMethod http://localhost:5080/health/live
Invoke-RestMethod http://localhost:5080/health/ready
Invoke-RestMethod http://localhost:5080/api/system/info
Invoke-WebRequest http://localhost:5080/openapi/v1.json -UseBasicParsing
```

Expected: HTTP 200, JSON bodies, and an `X-Request-Id` response header.

## Configuration for local development

`Development` overrides in `appsettings.Development.json` select SQLite, allow self-registration and
set `RequireSecureCookies=false` for plain HTTP. Use `dotnet user-secrets` for anything sensitive:

```powershell
dotnet user-secrets init --project src\backend\TraceForge.Api
dotnet user-secrets set "TraceForge:InstanceName" "Local Lab" --project src\backend\TraceForge.Api
```

Environment variables use double underscores: `TraceForge__RateLimiting__PermitLimit=240`.

### Phase 2 scope settings

| Setting | Default | Purpose |
|---|---|---|
| `TraceForge:Scopes:AllowPrivateNetworks` | `false` | Allows private/loopback/link-local/reserved destinations to be considered at all |

`AllowPrivateNetworks` is only one of two required conditions: the matching approved scope must also
carry the `private-network` restriction. Keep the switch `false` outside an isolated lab.

```powershell
# Isolated lab only
dotnet user-secrets set "TraceForge:Scopes:AllowPrivateNetworks" "true" --project src\backend\TraceForge.Api
```

`POST /api/workspaces/{id}/scopes/evaluate` performs no network I/O; it evaluates stored scopes and
returns pinned addresses. Phase 2 contains no scanning or connection code.

## Database

Two provider-specific migration assemblies exist so each provider gets correct column types:

| Project | Provider | Migration output |
|---|---|---|
| `TraceForge.Migrations.Sqlite` | SQLite (development) | `DateTimeOffset` stored as UNIX milliseconds (`INTEGER`) |
| `TraceForge.Migrations.PostgreSql` | PostgreSQL (deployment) | `timestamp with time zone` (`timestamptz`) |

### Local development (SQLite, verified)

`appsettings.Development.json` uses `Data Source=traceforge-dev.db` with
`ApplyMigrationsOnStartup=true`. Running the API creates the file, applies migrations and seeds the
roles; readiness reports the `database` check as healthy.

```powershell
dotnet run --project src\backend\TraceForge.Api\TraceForge.Api.csproj
Invoke-RestMethod http://localhost:5080/health/ready
```

### Migrations

The `dotnet-ef` tool is pinned in `dotnet-tools.json`:

```powershell
dotnet tool restore

# Add a migration to both providers after changing the model
dotnet ef migrations add <Name> --project src\backend\TraceForge.Migrations.Sqlite --output-dir Migrations
dotnet ef migrations add <Name> --project src\backend\TraceForge.Migrations.PostgreSql --output-dir Migrations

# Generate a deployment SQL script
dotnet ef migrations script --project src\backend\TraceForge.Migrations.PostgreSql `
  --output docs\reports\evidence\phase-2-postgresql-schema.sql

# Apply to a real PostgreSQL server (not available on the audited machine)
dotnet ef database update --project src\backend\TraceForge.Migrations.PostgreSql `
  --connection "<connection string>"
```

### PostgreSQL deployment (not verified here)

Set `ASPNETCORE_ENVIRONMENT=Production` plus:

```text
TraceForge__Database__Provider=PostgreSql
TraceForge__Database__ConnectionString=Host=...;Database=...;Username=...;Password=...
TraceForge__Database__ApplyMigrationsOnStartup=false
```

Apply migrations out of band with `dotnet ef database update` or the generated SQL script. The
schema script was produced on the audited machine, but **no PostgreSQL server exists there, so the
script has never been executed**. Readiness fails until the schema is applied. The Phase 1 and Phase 2
reports record this explicit limitation.

For Supabase PostgreSQL with the .NET API hosted separately on MonsterASP.NET, see the
[deployment guide](../deployment/supabase-monsterasp.md).

## Logs

Serilog writes to the console and to `src/backend/TraceForge.Api/logs/traceforge-YYYYMMDD.log`
(14-day rolling retention). Both sinks include the request correlation id.
