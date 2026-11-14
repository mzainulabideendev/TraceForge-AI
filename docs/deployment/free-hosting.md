# Free hosting options for the TraceForge API and console

**Status of this document: advisory.** The API and console were verified locally (see
[`../reports/phase-2-frontend.md`](../reports/phase-2-frontend.md)); no paid or free third-party host was
provisioned during that verification. Treat every provider limit below as something to re-check on the
provider's page before you rely on it — free tiers change without notice.

## The one hard constraint: same origin

The API issues two cookies, `traceforge.auth` (session) and `XSRF-TOKEN` (antiforgery), both with
`SameSite=Strict`. A browser will not send those cookies on cross-site requests, so an SPA served from
`app.netlify.app` cannot call an API on `api.runasp.net`: every call would arrive unauthenticated.

Deploy the console and the API **on one origin**. The API already supports this: if
`src/backend/TraceForge.Api/wwwroot/index.html` exists, the API serves the console, its assets and the
client-side route fallback. That is why the recommended path below is a single .NET application, and why
static-only free hosts (Netlify, Vercel, Cloudflare Pages, GitHub Pages) are listed only as a
not-recommended option.

## Recommended free path

| Layer | Service | Free offer (verify before relying) |
|---|---|---|
| API + console | [MonsterASP.NET](https://www.monsterasp.net/ASP.NET-Freehosting/) | 1 website, 5 GB disk, 256 MB RAM, .NET 10/11, free `*.runasp.net` / `*.tryasp.net` subdomain, HTTPS via Let's Encrypt. No custom domain, no credit card. |
| Database | Supabase PostgreSQL (this repository's project) | 500 MB database. Free projects pause after a week without activity; resume from the dashboard. |
| Alternative database | [Neon](https://neon.com) | 0.5 GB, scales to zero, no expiry — often the smoother free Postgres for a low-traffic API. |

Why this combination: MonsterASP.NET runs a real ASP.NET Core process (so the same `wwwroot` hosting and
`/health/*` probes work unchanged), Supabase supplies managed PostgreSQL with a free tier, and both are
reachable over HTTPS so secure cookies stay enabled.

### Deploy to MonsterASP.NET

1. Set the database up as described in
   [Supabase PostgreSQL with MonsterASP.NET](supabase-monsterasp.md), including rotating the password that
   was ever pasted into a chat.
2. Build the console into the API so it ships in one unit:

   ```powershell
   cd src\frontend
   npm install
   npm run build:api
   ```

3. Publish and upload:

   ```powershell
   dotnet publish src\backend\TraceForge.Api\TraceForge.Api.csproj -c Release -o publish
   ```

   Upload the contents of `publish\` through the MonsterASP.NET control panel (Web Deploy, FTP, or GitHub
   Actions). `wwwroot\` must be included — it contains the console.
4. Application settings (control panel, never in source):

   ```text
   ASPNETCORE_ENVIRONMENT=Production
   TraceForge__Database__Provider=PostgreSql
   TraceForge__Database__ConnectionString=Host=<host>;Port=<port>;Database=postgres;Username=<user>;Password=<new-password>;SSL Mode=Require
   TraceForge__Database__ApplyMigrationsOnStartup=true
   TraceForge__Auth__RequireSecureCookies=true
   TraceForge__Auth__AllowSelfRegistration=true
   ```

   Keep self-registration `true` only until the first account (which becomes the global Administrator)
   exists, then set it to `false` and restart. Set `ApplyMigrationsOnStartup=false` after the first
   successful start.

5. Verify: `https://<site>.runasp.net/health/live`, `/health/ready`, then open the root URL and sign in.

**Limits to expect on the free plan:** 256 MB RAM is enough for this API under light load but not for a
large audit log; the free plan is described by the provider as being for development, testing and
learning, and commercial use is not allowed. Cold starts and shared CPU are normal.

## Other free options

| Option | Free allowance | Trade-offs for this project |
|---|---|---|
| [Azure App Service F1](https://azure.microsoft.com/pricing/details/app-service/) | Free F1 Linux/Windows plan, .NET 10 runtime | 60 CPU-minutes per day, no AlwaysOn (app unloads when idle), no custom-domain TLS on F1. Works, but the app sleeps — bad for demos and health probes. |
| [Azure Container Apps](https://azure.microsoft.com/pricing/details/container-apps/) | Monthly grant (180,000 vCPU-seconds, 360,000 GiB-seconds, 2M requests) per subscription | Must containerise the API; scales to zero, so first request is slow. Best free option if you want a container and no daily shutdown. |
| [Render](https://render.com/pricing) free web service | 0.1 CPU, 512 MB RAM, 100 GB bandwidth, idles after ~15 min | Docker or native .NET build; cold starts after idle. No persistent local disk, so SQLite is not an option — use PostgreSQL. |
| Oracle Cloud Always Free VM | 1–4 ARM cores, 24 GB RAM (shape-dependent), always on | You run the OS, reverse proxy, TLS and updates yourself. Most "free" capacity, most operational work. |
| Static host (Netlify/Vercel/Cloudflare Pages/GitHub Pages) + separate API | Generous static hosting | **Not recommended here**: cross-site cookies are blocked with `SameSite=Strict`, so the console would need a same-site proxy or a relaxed cookie policy (weakening CSRF defense). Only viable if you put both behind one custom domain. |
| GitHub Codespaces / dev containers | Free monthly hours | A development environment, not a deployment target: the URL stops when the codespace stops. |

### Cost-free does not mean production-ready

The free tiers above are fine for evaluation. If TraceForge is going to assess real systems, the API needs
more memory, persistent logs, a backup strategy for the audit log, and no cold-start gap that could
interrupt a long operation. Plan the upgrade before you depend on it.

## Database choice and the audit log

The audit log is append-only at the application level (no update or delete path in code) but it lives in
`AuditLogs`, a normal table. On a free database, retention is limited by storage. Export the audit log
before you delete or pause a free project, and remember that a paused Supabase project cannot be read
until it is resumed.

## What was actually verified

| Claim | Evidence |
|---|---|
| The API serves the built console from the same origin, including deep links | `docs/reports/evidence/frontend-e2e.json` steps `spa-document`, `spa-bundle-asset`, `spa-deep-link-fallback` |
| The console's full API contract works end-to-end | Same file, 32 steps, all `PASS` |
| The console renders and bootstraps in a real browser without runtime errors | `docs/reports/evidence/frontend-ui-verification.json` (`pageErrors: []`, `consoleErrors: []`) |
| A deployment to MonsterASP.NET, Azure, Render or Oracle Cloud | **NOT RUN** — no host was provisioned |
| PostgreSQL runtime verification against any host | **NOT RUN** in this environment; see the Phase 2 report |
