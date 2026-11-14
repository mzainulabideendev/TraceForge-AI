import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { apiUrl } from './api-config';
import {
  AddWorkspaceMemberRequest,
  ChangeWorkspaceMemberRoleRequest,
  CreateWorkspaceRequest,
  UpdateWorkspaceRequest,
  WorkspaceDetail,
  WorkspaceMember,
  WorkspaceSummary,
} from './models';

/** Workspace and membership operations. Authorization is enforced by the API on every call. */
@Injectable({ providedIn: 'root' })
export class WorkspaceService {
  private readonly http = inject(HttpClient);
  private readonly base = apiUrl('/workspaces');

  list(): Observable<WorkspaceSummary[]> {
    return this.http.get<WorkspaceSummary[]>(this.base);
  }

  get(workspaceId: string): Observable<WorkspaceDetail> {
    return this.http.get<WorkspaceDetail>(`${this.base}/${workspaceId}`);
  }

  create(request: CreateWorkspaceRequest): Observable<WorkspaceDetail> {
    return this.http.post<WorkspaceDetail>(this.base, request);
  }

  update(workspaceId: string, request: UpdateWorkspaceRequest): Observable<WorkspaceDetail> {
    return this.http.put<WorkspaceDetail>(`${this.base}/${workspaceId}`, request);
  }

  archive(workspaceId: string): Observable<WorkspaceDetail> {
    return this.http.post<WorkspaceDetail>(`${this.base}/${workspaceId}/archive`, {});
  }

  listMembers(workspaceId: string): Observable<WorkspaceMember[]> {
    return this.http.get<WorkspaceMember[]>(`${this.base}/${workspaceId}/members`);
  }

  addMember(workspaceId: string, request: AddWorkspaceMemberRequest): Observable<WorkspaceMember> {
    return this.http.post<WorkspaceMember>(`${this.base}/${workspaceId}/members`, request);
  }

  changeMemberRole(
    workspaceId: string,
    userId: string,
    request: ChangeWorkspaceMemberRoleRequest,
  ): Observable<WorkspaceMember> {
    return this.http.patch<WorkspaceMember>(
      `${this.base}/${workspaceId}/members/${userId}`,
      request,
    );
  }

  removeMember(workspaceId: string, userId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${workspaceId}/members/${userId}`);
  }
}
