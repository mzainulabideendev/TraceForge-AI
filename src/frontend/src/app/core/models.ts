/**
 * Wire models for the TraceForge API.
 *
 * The API serializes C# enums as numbers (System.Text.Json defaults), so workspace roles and scope
 * types are numeric on the wire even though request bodies accept their names.
 */

export interface UserAccount {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  createdAtUtc: string;
  lastLoginAtUtc: string | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

/** Workspace role values, matching `TraceForge.Domain.Workspaces.WorkspaceRole`. */
export type WorkspaceRole = 1 | 2 | 3;

export const WorkspaceRole = {
  Owner: 1,
  Administrator: 2,
  Analyst: 3,
} as const satisfies Record<string, WorkspaceRole>;

export const WORKSPACE_ROLE_NAMES: Record<WorkspaceRole, string> = {
  [WorkspaceRole.Owner]: 'Owner',
  [WorkspaceRole.Administrator]: 'Administrator',
  [WorkspaceRole.Analyst]: 'Analyst',
};

export function isWorkspaceRole(value: unknown): value is WorkspaceRole {
  return value === 1 || value === 2 || value === 3;
}

export interface WorkspaceSummary {
  id: string;
  name: string;
  description: string | null;
  status: string;
  role: WorkspaceRole;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface WorkspaceDetail {
  id: string;
  name: string;
  description: string | null;
  status: string;
  callerRole: WorkspaceRole;
  createdByUserId: string;
  memberCount: number;
  scopeCount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  archivedAtUtc: string | null;
}

export interface WorkspaceMember {
  userId: string;
  email: string;
  displayName: string;
  role: WorkspaceRole;
  addedAtUtc: string;
  updatedAtUtc: string;
}

/** Scope type values, matching `TraceForge.Domain.Scopes.ScopeType`. */
export type ScopeType = 1 | 2 | 3 | 4 | 5;

export const ScopeType = {
  IPv4: 1,
  IPv6: 2,
  Cidr: 3,
  Hostname: 4,
  Origin: 5,
} as const satisfies Record<string, ScopeType>;

export const SCOPE_TYPE_NAMES: Record<ScopeType, string> = {
  [ScopeType.IPv4]: 'IPv4',
  [ScopeType.IPv6]: 'IPv6',
  [ScopeType.Cidr]: 'CIDR',
  [ScopeType.Hostname]: 'Hostname',
  [ScopeType.Origin]: 'Origin',
};

/** Statuses are serialized as strings by the API. */
export type ScopeStatus = 'Pending' | 'Approved' | 'Revoked' | 'Expired';

export interface Scope {
  id: string;
  workspaceId: string;
  type: ScopeType;
  target: string;
  status: ScopeStatus | string;
  approvalReference: string | null;
  approvedByUserId: string | null;
  approvedAtUtc: string | null;
  revokedByUserId: string | null;
  revokedAtUtc: string | null;
  validFromUtc: string;
  expiresAtUtc: string;
  restrictions: string[];
  createdByUserId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  currentlyUsable: boolean;
}

export interface ScopeEvaluation {
  allowed: boolean;
  reason: string;
  detail: string;
  matchedScopeId: string | null;
  pinnedAddresses: string[];
}

export interface AuditEntry {
  id: number;
  actorUserId: string | null;
  actorIdentifier: string | null;
  action: string;
  targetType: string;
  targetId: string | null;
  /** AuditOutcome enum: 1 Success, 2 Failure, 3 Denied, 4 Unknown. */
  outcome: number;
  ipAddress: string | null;
  userAgent: string | null;
  details: string | null;
  occurredAtUtc: string;
}

/* ---------- Requests ---------- */

export interface CreateWorkspaceRequest {
  name: string;
  description: string | null;
}

export interface UpdateWorkspaceRequest {
  name: string;
  description: string | null;
}

export interface AddWorkspaceMemberRequest {
  email: string;
  role: string;
}

export interface ChangeWorkspaceMemberRoleRequest {
  role: string;
}

export interface RegisterScopeRequest {
  type: string;
  target: string;
  validFromUtc?: string;
  expiresAtUtc?: string;
  restrictions: string[];
}

export interface ApproveScopeRequest {
  approvalReference: string;
}

export interface RevokeScopeRequest {
  reason: string | null;
}

export interface UpdateScopeRestrictionsRequest {
  restrictions: string[];
}

export interface EvaluateScopeRequest {
  target: string;
  scheme?: string | null;
  port?: number | null;
}
