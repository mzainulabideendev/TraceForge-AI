# TraceForge AI

**Turn security evidence into actionable defensive intelligence.**

TraceForge AI is an open-source cybersecurity investigation platform. It connects real, evidence-backed
security findings into attack-path intelligence so engineers can understand *why* a weakness matters,
not just that it exists.

> **Authorized testing only.** TraceForge AI is built for environments you own or are explicitly
> authorized to assess. Every scan must be bound to a workspace with an approved scope.

## Status

This repository is under active, phase-gated development. The table below is the single source of truth
for what actually works today.

| Phase | Scope | Status |
|---|---|---|
| 0 | Repository baseline: build, API host, structured logging, health probes, security headers, request correlation, rate limiting, configuration validation, automated tests, CI, documentation | **Implemented and verified** |
| 1 | Backend foundation: authentication, PostgreSQL persistence, workspace model | Not started |
| 2 | Workspace and authorized-scope enforcement | Not started |
| 3 | Asset discovery | Not started |
| 4 | Deterministic detection engine | Not started |
| 5 | Angular dashboard | Not started |
| 6 | Evidence-backed attack graph | Not started |
| 7 | AI investigator (provider adapters) | Not started |
| 8 | Security digital twin | Not started |
| 9 | Remediation and verification | Not started |
| 10 | Reporting | Not started |
| 11 | Incident investigation and replay | Not started |
| 12 | Honeypot integration | Not started |
| 13 | Production hardening | Not started |

**Do not assume any capability that is not listed as implemented above.** Nothing in this repository
fabricates scan results, findings, or metrics.

## Requirements

| Component | Version | Verified here |
|---|---|---|
| .NET SDK | 10.0.x | 10.0.401 |
| Node.js (future frontend) | 22 LTS or later | 24.20.0 |
| Python (future analysis service) | 3.12+ | 3.12.10 |
| PostgreSQL (Phase 1+) | 16+ | Not installed on the development machine |
| Docker (optional) | 24+ | Not installed on the development machine |

The current Phase 0 baseline runs with the .NET SDK alone. PostgreSQL, Docker and Nmap are not yet
required and are not yet used.

## Quick start (Windows PowerShell)

```powershell
dotnet restore TraceForge.slnx
dotnet build TraceForge.slnx
dotnet test TraceForge.slnx
dotnet run --project src\backend\TraceForge.Api\TraceForge.Api.csproj
```

The API listens on `http://localhost:5080` (see `Properties/launchSettings.json`).

```powershell
Invoke-RestMethod http://localhost:5080/health/live
Invoke-RestMethod http://localhost:5080/health/ready
Invoke-RestMethod http://localhost:5080/api/system/info
```

OpenAPI document (Development environment only): `http://localhost:5080/openapi/v1.json`

## API surface (Phase 0)

| Method | Path | Purpose |
|---|---|---|
| GET | `/health/live` | Liveness probe; succeeds while the process serves requests |
| GET | `/health/ready` | Readiness probe; runs real dependency checks (log directory writability today) |
| GET | `/api/system/info` | Version, environment, real uptime, instance name |
| GET | `/openapi/v1.json` | OpenAPI document (Development only) |

Rate limiting: fixed window per client IP on `/api/*`, default 120 requests per 60 seconds.
Every response carries `X-Request-Id`; problem responses include it as `traceId`.

## Configuration

Settings live under the `TraceForge` configuration section and are validated at startup; an invalid
value stops the process instead of degrading silently.

| Key | Default | Constraint |
|---|---|---|
| `TraceForge:InstanceName` | `TraceForge AI` | Non-empty, max 120 characters |
| `TraceForge:RateLimiting:PermitLimit` | `120` | 1 – 100,000 |
| `TraceForge:RateLimiting:WindowSeconds` | `60` | 1 – 3,600 |
| `TraceForge:RateLimiting:QueueLimit` | `0` | ≥ 0 |

Use environment variables with double underscores in deployment, for example
`TraceForge__RateLimiting__PermitLimit=240`. See `.env.example`. Secrets are never stored in this
repository.

## Documentation

- [Architecture overview](docs/architecture/overview.md)
- [Development setup](docs/development/setup.md)
- [Testing guide](docs/development/testing.md)
- [Threat model](docs/security/threat-model.md)
- [Security policy](SECURITY.md)
- [Initial repository audit](docs/reports/initial-audit.md)
- [Phase 0 completion report](docs/reports/phase-0-baseline.md)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Contributions that add simulated results, fabricated findings,
or unverified claims will be rejected.

## License

Apache License 2.0 — see [LICENSE](LICENSE).
