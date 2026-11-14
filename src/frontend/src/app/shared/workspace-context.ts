import { Injectable, computed, signal } from '@angular/core';
import { WorkspaceDetail, WorkspaceRole } from '../core/models';
import { RolePermissions } from '../core/format';

/**
 * Holds the workspace currently open in the workspace layout so tab views share one authoritative
 * detail response instead of each refetching it. The role here only hides controls; the API decides.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceContext {
  readonly workspaceId = signal<string | null>(null);
  readonly detail = signal<WorkspaceDetail | null>(null);

  readonly role = computed<WorkspaceRole | null>(() => this.detail()?.callerRole ?? null);
  readonly isArchived = computed(() => (this.detail()?.status ?? '').toLowerCase() === 'archived');

  readonly canUpdate = computed(() => {
    const role = this.role();
    return role !== null && RolePermissions.canUpdateWorkspace(role) && !this.isArchived();
  });

  readonly canArchive = computed(() => {
    const role = this.role();
    return role !== null && RolePermissions.canArchiveWorkspace(role) && !this.isArchived();
  });

  readonly canManageMembers = computed(() => {
    const role = this.role();
    return role !== null && RolePermissions.canManageMembers(role) && !this.isArchived();
  });

  readonly canAssignOwner = computed(() => {
    const role = this.role();
    return role !== null && RolePermissions.canAssignOwner(role);
  });

  readonly canApproveScopes = computed(() => {
    const role = this.role();
    return role !== null && RolePermissions.canApproveScopes(role) && !this.isArchived();
  });

  set(workspaceId: string, detail: WorkspaceDetail): void {
    this.workspaceId.set(workspaceId);
    this.detail.set(detail);
  }

  reset(): void {
    this.workspaceId.set(null);
    this.detail.set(null);
  }
}
