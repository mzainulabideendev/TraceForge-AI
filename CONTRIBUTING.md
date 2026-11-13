# Contributing to TraceForge AI

## Before you start

Read the [security policy](SECURITY.md) and the [architecture overview](docs/architecture/overview.md).

By contributing you agree that your work is licensed under Apache-2.0.

## Ground rules

1. **No simulated functionality.** A feature is not done because an interface, endpoint or screen
   exists. It is done when it has a real execution path and verified evidence.
2. **No invented data.** No hard-coded dashboard numbers, fake findings, canned AI responses, or
   graph edges without evidence references.
3. **No unverified claims.** If a test was skipped, blocked or not run, say so.
4. **Security first.** Never bypass scope validation, SSRF protection or AI output validation to make
   a feature work. Open an issue instead.
5. **Small, verified commits.** Build and test before committing.

## Development workflow

```powershell
dotnet restore TraceForge.slnx
dotnet build TraceForge.slnx --configuration Release
dotnet test TraceForge.slnx
```

All three commands must succeed with zero warnings before a pull request is opened.

## Coding conventions

- C# 13 / .NET 10, nullable reference types enabled, file-scoped namespaces.
- Central package management: add versions to `Directory.Packages.props`, not to project files.
- Async all the way; accept and forward `CancellationToken` in I/O paths.
- UTC for all persisted timestamps.
- Structured logging via Serilog; never interpolate secrets or personal data into log messages.
- Return `application/problem+json` for error responses.
- Public behaviour requires a test that fails without the change.

## Phase discipline

Work is organised into phases (see `README.md`). Do not start a phase while a critical security
requirement of the current phase is failing. Each completed phase requires:

- Implemented functionality with a real execution path.
- Unit, integration and (where applicable) security tests that were actually executed.
- Updated documentation and a completion report in `docs/reports/`.

## Adding a detection rule (from Phase 4)

A rule must declare: rule ID, version, category, severity, confidence, detection logic, evidence
requirements, remediation guidance, at least one positive and one negative test case. A rule that
cannot produce reproducible evidence must not ship.

## Reporting issues

Use the issue tracker for defects and proposals. Do not paste secrets, tokens, or personal data.
Security defects go through the private channel described in `SECURITY.md`.
