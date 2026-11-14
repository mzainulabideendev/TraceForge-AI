# Supabase PostgreSQL with MonsterASP.NET

TraceForge AI is a .NET 10 API with EF Core PostgreSQL migrations. Supabase provides its PostgreSQL
database; deploy the API separately as an ASP.NET Core application on MonsterASP.NET. The Supabase
publishable and secret API keys are not used by this direct PostgreSQL integration.

## Rotate exposed credentials first

The database password and Supabase secret key were pasted into a chat. Treat them as compromised:

1. Reset the database password in Supabase.
2. Revoke and replace the exposed secret key if it is still active.
3. Do not put either value in source code, this repository, or chat. Store the new database password
   only in MonsterASP.NET's protected application settings.

## Configure the database

In Supabase, copy the connection details for the project. Use **Direct connection** if the
MonsterASP.NET worker can reach the Supabase IPv6 endpoint. If the host is IPv4-only, use Supabase's
**Session pooler** details instead. Use the host, port, database, and username shown for the selected
connection method.

Set these application environment variables in MonsterASP.NET:

| Name | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `TraceForge__Database__Provider` | `PostgreSql` |
| `TraceForge__Database__ConnectionString` | `Host=<host>;Port=<port>;Database=postgres;Username=<username>;Password=<new-password>;SSL Mode=Require` |
| `TraceForge__Database__ApplyMigrationsOnStartup` | `true` for the first single-instance startup only |

Use the Npgsql key/value connection-string format shown above, not the `postgresql://` URI copied from
the Supabase dashboard. Keep `TraceForge__Auth__RequireSecureCookies=true` and
`TraceForge__Auth__AllowSelfRegistration=false` in production.

## Apply the schema and bootstrap

The PostgreSQL migration assembly is included in the API deployment. On the first deployment, run
one API instance with `TraceForge__Database__ApplyMigrationsOnStartup=true`. Startup applies pending
EF Core migrations and creates the built-in Identity roles. Confirm `/health/ready` reports the
database as healthy, then change the setting to `false` and restart the API. Do not perform the
initial migration concurrently from multiple API instances.

The current full PostgreSQL migration script is also available at
[`docs/reports/evidence/phase-2-postgresql-schema.sql`](../reports/evidence/phase-2-postgresql-schema.sql).
Use it only for a fresh database if applying migrations outside the application; then start the API
once with startup migrations enabled so the built-in roles are seeded. Do not run both the SQL script
and migrations against an already initialized database.

For later model changes, generate a new PostgreSQL migration and apply it through a controlled
deployment step before deploying API instances that depend on the new schema. Keep startup migration
disabled during normal multi-instance operation.

## Deploy and verify the API

Publish the `src/backend/TraceForge.Api` application for the .NET runtime supported by the
MonsterASP.NET plan. Configure the environment variables above in the host's protected settings,
deploy the published application, and check:

- `/health/live` responds successfully.
- `/health/ready` reports the database check as healthy.
- The host is serving the API over HTTPS; secure cookies are enabled in production.

### Ship the console with the API (recommended)

The session and antiforgery cookies are `SameSite=Strict`, so the console must be served from the same
origin as `/api`. Build it into the API before publishing:

```powershell
cd src\frontend
npm install
npm run build:api
```

That writes the production bundle to `src\backend\TraceForge.Api\wwwroot`. On startup the API logs
`Serving the built web console from ...` and serves the SPA, its hashed assets and the client-side route
fallback, while `/api` and `/health` keep their own behaviour and JSON 404s. Nothing needs to be enabled
in configuration: the presence of `wwwroot/index.html` is the switch.

Then publish and upload:

```powershell
dotnet publish src\backend\TraceForge.Api\TraceForge.Api.csproj -c Release -o publish
```

Open `https://<your-site>/` and sign in. `TraceForge__Auth__AllowSelfRegistration=true` is needed only to
create the first account, which becomes the global Administrator; set it back to `false` afterwards.

Do not upload `node_modules` or `src/frontend/dist` to the host; only the API publish output is required.

See [free hosting options](free-hosting.md) for the free tiers that fit this shape and their limits.

This repository change documents the integration; it does not deploy the API or execute the
migrations against the Supabase project.
