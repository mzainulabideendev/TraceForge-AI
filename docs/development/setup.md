# Development Setup

## Verified environment

Audited on 2026-11-13 on the primary development machine:

| Tool | Detected | Notes |
|---|---|---|
| OS | Windows 11 Pro | PowerShell 5.1 available |
| RAM | 15.37 GB | Development profile keeps services minimal |
| .NET SDK | 10.0.401 | Required |
| Node.js / npm | v24.20.0 / 11.19.0 | Only needed from Phase 5 (Angular) |
| Python | 3.12.10 | Only needed when Python analysis modules land |
| Git | 2.55.0 | |
| Docker | **not installed** | Optional; PostgreSQL via Docker is documented but unverified here |
| PostgreSQL | **not installed** | Phase 1 will provide a SQLite development profile |
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

## Verify a running instance

```powershell
Invoke-RestMethod http://localhost:5080/health/live
Invoke-RestMethod http://localhost:5080/health/ready
Invoke-RestMethod http://localhost:5080/api/system/info
Invoke-WebRequest http://localhost:5080/openapi/v1.json -UseBasicParsing
```

Expected: HTTP 200, JSON bodies, and an `X-Request-Id` response header.

## Configuration for local development

Copy `.env.example` to `.env` for reference, or use `dotnet user-secrets` from Phase 1 onward for
anything sensitive:

```powershell
dotnet user-secrets init --project src\backend\TraceForge.Api
dotnet user-secrets set "TraceForge:InstanceName" "Local Lab" --project src\backend\TraceForge.Api
```

Environment variables use double underscores: `TraceForge__RateLimiting__PermitLimit=240`.

## Logs

Serilog writes to the console and to `src/backend/TraceForge.Api/logs/traceforge-YYYYMMDD.log`
(14-day rolling retention). Both sinks include the request correlation id.

## Database (Phase 1, not yet available)

The audited machine has no PostgreSQL server and no Docker. Phase 1 will implement:

- `Development` profile: SQLite file database, zero external dependencies.
- `Staging`/`Production` profile: PostgreSQL with EF Core migrations.
- `docker compose` definition for PostgreSQL, documented but validated only where Docker exists.

Do not document or assume database functionality before it exists.
