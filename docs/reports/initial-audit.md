# Initial Repository Audit

**Phase:** 0 (Repository and baseline)
**Date:** 2026-11-13
**Auditor:** AI engineering agent (OpenCode)
**Repository:** `D:\project\Trace Forge` (local, empty at audit time)

## 1. Executive summary

The working directory contained **no source code, no Git repository, no tests and no configuration**
before this audit. TraceForge AI is therefore a greenfield build, and no existing user work needed to
be preserved or migrated.

Two environment constraints materially affect the plan:

1. **Docker is not installed**, so container-based services cannot be verified on this machine.
2. **PostgreSQL and Nmap are not installed**, so a PostgreSQL-only or Nmap-dependent baseline cannot be
   executed here.

The baseline was therefore designed to run on the .NET SDK alone, and the database design will use a
SQLite development profile with PostgreSQL for deployed environments (ADR-005, proposed in Phase 1).

## 2. Environment detection (observed)

| Item | Command | Result |
|---|---|---|
| Operating system | `Get-CimInstance Win32_OperatingSystem` | Microsoft Windows 11 Pro |
| RAM | `Win32_ComputerSystem.TotalPhysicalMemory` | 15.37 GB |
| .NET SDKs | `dotnet --list-sdks` | `10.0.401` (`C:\Program Files\dotnet\sdk`) |
| Node.js | `node --version` | `v24.20.0` |
| npm | `npm --version` | `11.19.0` |
| Python | `python --version` / `py -0p` | `3.12.10` (`...\Python312\python.exe`) |
| Git | `git --version` | `2.55.0.windows.5` |
| Docker | `docker --version` | **Not found** |
| PostgreSQL | `Get-Command psql`, `Get-Service *postgres*` | **Not found / no service** |
| Nmap | `Get-Command nmap` | **Not found** |
| NuGet connectivity | `dotnet nuget list source`, restore | `nuget.org` reachable; restore succeeded |
| npm connectivity | `npm ping` | `PONG` in ~1002 ms |

## 3. Repository state before work

| Check | Result |
|---|---|
| Files present | None (empty directory) |
| Git repository | Absent — `git rev-parse --is-inside-work-tree` failed |
| .NET solution | None |
| Existing tests | None |
| Existing dependencies | None |
| Security issues in existing code | None (no code existed) |
| Documented functionality | None |
| Implementation gaps | Entire product (Phases 0–13) |

## 4. Actions taken during the audit

1. Enumerated the directory and confirmed it was empty.
2. Detected the toolchain versions listed above.
3. Verified package-registry connectivity for NuGet and npm.
4. Initialised a Git repository on branch `main` with the pre-existing global Git identity
   (`Muhammad Zain Ul Abideen`) and no commits.
5. Created `docs/reports/` and recorded this audit.

## 5. Constraints and their consequences

| Constraint | Consequence | Mitigation |
|---|---|---|
| No Docker | Compose-based PostgreSQL, Juice Shop lab and containerised analysis cannot be executed or verified here | Documented as unverified; Phase 1 adds a SQLite development profile so the product runs without Docker |
| No PostgreSQL | Migrations against a real PostgreSQL server cannot be verified here | Phase 1 will verify migrations on SQLite and mark PostgreSQL coverage as BLOCKED until a server exists |
| No Nmap | Nmap integration cannot be executed or verified | Phase 3 will implement native TCP connect discovery first; Nmap stays optional and unverified |
| 16 GB RAM | Running several heavy services concurrently is not advisable | Lightweight development profile; controlled concurrency; optional services |
| Windows only | Linux-only behaviours (file permissions, case sensitivity) are untested | CI runs on `ubuntu-latest` from Phase 0 onward |

## 6. Risk register (initial)

| ID | Risk | Severity | Status |
|---|---|---|---|
| R-01 | No authentication yet; API must not be exposed to untrusted networks | High | Mitigated by design (only health/version endpoints); resolved in Phase 1 |
| R-02 | Database capability unverified on this machine | Medium | Mitigation planned (Phase 1) |
| R-03 | Scan safety controls (scope, SSRF, rebinding) not yet implemented | Critical | Must be complete before Phase 3 is declared done |
| R-04 | AI provider availability/quota unknown | Low | Voluntary feature; core must work without AI |
| R-05 | CI workflow not yet executed on GitHub (no remote configured) | Low | Workflow is committed; first remote push will execute it |

## 7. Phase 0 outcome summary

Phase 0 deliverables were implemented and verified after this audit; see
[phase-0-baseline.md](phase-0-baseline.md) for commands, evidence and results.

## 8. Machine-readable audit

See [initial-audit.json](initial-audit.json).
