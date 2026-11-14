import {
  SCOPE_TYPE_NAMES,
  ScopeStatus,
  ScopeType,
  WORKSPACE_ROLE_NAMES,
  WorkspaceRole,
} from './models';

/** Human label for a numeric workspace role; unknown values never crash the UI. */
export function roleLabel(role: number | null | undefined): string {
  if (role === null || role === undefined) {
    return 'Unknown';
  }

  return WORKSPACE_ROLE_NAMES[role as WorkspaceRole] ?? `Role ${role}`;
}

/** CSS suffix for a role badge. */
export function roleClass(role: number | null | undefined): string {
  switch (role) {
    case 1:
      return 'badge-owner';
    case 2:
      return 'badge-administrator';
    case 3:
      return 'badge-analyst';
    default:
      return '';
  }
}

export function scopeTypeLabel(type: number | null | undefined): string {
  if (type === null || type === undefined) {
    return 'Unknown';
  }

  return SCOPE_TYPE_NAMES[type as ScopeType] ?? `Type ${type}`;
}

/** CSS suffix for workspace and scope status badges. */
export function statusClass(status: string | null | undefined): string {
  switch ((status ?? '').toLowerCase()) {
    case 'active':
    case 'approved':
    case 'success':
      return 'badge-active';
    case 'archived':
    case 'revoked':
    case 'failure':
    case 'denied':
      return 'badge-revoked';
    case 'pending':
      return 'badge-pending';
    case 'expired':
      return 'badge-expired';
    default:
      return '';
  }
}

export function auditOutcomeLabel(outcome: number): string {
  switch (outcome) {
    case 1:
      return 'Success';
    case 2:
      return 'Failure';
    case 3:
      return 'Denied';
    default:
      return `Outcome ${outcome}`;
  }
}

/** Compact, locale-aware timestamp. Returns an em dash for missing values. */
export function formatTimestamp(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function formatDate(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
  });
}

/** `2026-11-14T12:00:00Z` -> `yyyy-MM-ddTHH:mm` in the browser timezone, for datetime-local inputs. */
export function toDateTimeLocal(value: Date): string {
  const pad = (part: number) => part.toString().padStart(2, '0');
  return (
    `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}` +
    `T${pad(value.getHours())}:${pad(value.getMinutes())}`
  );
}

/** Converts a datetime-local value (browser timezone) to a UTC ISO instant. */
export function toUtcIso(value: string): string | null {
  if (!value) {
    return null;
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

export function daysFromNow(days: number): Date {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return date;
}

/** True when the API reports the scope as expired by time, independent of the stored status. */
export function isPast(value: string | null | undefined): boolean {
  if (!value) {
    return false;
  }

  const date = new Date(value);
  return !Number.isNaN(date.getTime()) && date.getTime() < Date.now();
}

/** Local permission mirrors used only to hide controls; the API is the enforcement point. */
export const RolePermissions = {
  canUpdateWorkspace: (role: WorkspaceRole) => role === 1 || role === 2,
  canArchiveWorkspace: (role: WorkspaceRole) => role === 1,
  canManageMembers: (role: WorkspaceRole) => role === 1 || role === 2,
  canAssignOwner: (role: WorkspaceRole) => role === 1,
  canApproveScopes: (role: WorkspaceRole) => role === 1 || role === 2,
} as const;

export function isUsableStatus(status: ScopeStatus | string): boolean {
  return status === 'Approved';
}
