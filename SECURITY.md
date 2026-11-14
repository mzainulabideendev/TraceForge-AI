# Security Policy

## Supported versions

TraceForge AI is pre-1.0. Only the latest commit on `main` receives security fixes.

## Reporting a vulnerability

Do **not** open a public issue for a security defect. Report it privately to the maintainers through
the repository's private security advisory channel (GitHub → Security → Advisories → "Report a
vulnerability"). Include:

- Affected component and commit hash.
- Reproduction steps against an environment you are authorized to test.
- Impact assessment (what an attacker gains).
- Evidence (logs, request/response captures) with secrets redacted.

## Scope of testing

You may not test against systems you do not own or lack written authorization to assess. Reports
produced by scanning third-party infrastructure will be closed without review.

Phase 2 does not yet open network connections: the `POST /api/workspaces/{id}/scopes/evaluate`
endpoint only evaluates stored policy and returns pinned addresses. Do not describe it as a scan.

## Non-negotiable product security rules

These rules apply to every contribution:

1. **No fabricated evidence.** Findings, metrics and graph edges must derive from executed checks.
2. **No unauthorized scanning.** Every scan resolves to a workspace with an approved scope; targets
   outside that scope are rejected before a connection is opened.
3. **No SSRF or DNS-rebinding bypass.** Resolved addresses and redirects are validated against the
   approved scope at connection time.
4. **No destructive testing.** No denial of service, credential brute force, persistence, evasion, or
   data exfiltration.
5. **AI output is untrusted.** AI responses are validated against a schema, never executed, and never
   permitted to widen scope or trigger offensive actions. Evidence must be referenced explicitly.
6. **Secrets stay out of storage and prompts.** Credentials, tokens and personal data are never written
   into findings, logs, or cloud AI requests.
7. **Every sensitive action is audited.** Authentication, scope changes, scan launches and report
   exports are recorded with actor, timestamp (UTC) and outcome.

### Phase 2 rules for workspace and scope work

8. **Membership is the only source of workspace authority.** A global Identity role grants no access to
   a workspace. Never derive workspace identity or role from a request body, query string, header or
   client state.
9. **Default deny, least privilege.** Non-members receive `404`; members lacking a role receive `403`;
   archived workspaces reject mutations with `409`. Do not weaken one of these to simplify a client.
10. **No auto-approval.** Registering, importing or discovering a target must leave the scope `Pending`.
    Only an explicit, audited approval by an Owner or Administrator with a non-empty evidence reference
    makes a target usable.
11. **A hostname approval never approves addresses.** Every resolved address must match an approved
    IP/CIDR scope. Callers consume the pinned addresses returned by the policy and must not re-resolve.
12. **Private networks stay closed by default.** `TraceForge:Scopes:AllowPrivateNetworks` must remain
    `false` outside an isolated lab, and a matching scope must also carry the `private-network`
    restriction.
13. **No frontend-only or AI authorization.** Guards, prompts and model output are UI convenience or
    data, never an authorization decision. Enforcement lives in the server-side access resolver.
14. **Preserve the last Owner.** The last Owner of a workspace must not be removed or demoted, and an
    Administrator must never be able to grant, modify or remove an Owner.

## Disclosure

We aim to acknowledge reports within 5 business days and to ship a fix or mitigation within 30 days
for confirmed high-severity issues.
