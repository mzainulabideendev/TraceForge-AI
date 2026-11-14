import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { apiUrl } from './api-config';
import {
  ApproveScopeRequest,
  EvaluateScopeRequest,
  RegisterScopeRequest,
  RevokeScopeRequest,
  Scope,
  ScopeEvaluation,
  UpdateScopeRestrictionsRequest,
} from './models';

/**
 * Authorized-scope operations.
 *
 * Registration always creates a Pending scope; only an explicit approve call by an Owner or
 * Administrator makes a target usable. The SPA never assumes approval.
 */
@Injectable({ providedIn: 'root' })
export class ScopeService {
  private readonly http = inject(HttpClient);

  private base(workspaceId: string): string {
    return apiUrl(`/workspaces/${workspaceId}/scopes`);
  }

  list(workspaceId: string): Observable<Scope[]> {
    return this.http.get<Scope[]>(this.base(workspaceId));
  }

  get(workspaceId: string, scopeId: string): Observable<Scope> {
    return this.http.get<Scope>(`${this.base(workspaceId)}/${scopeId}`);
  }

  register(workspaceId: string, request: RegisterScopeRequest): Observable<Scope> {
    return this.http.post<Scope>(this.base(workspaceId), request);
  }

  approve(workspaceId: string, scopeId: string, request: ApproveScopeRequest): Observable<Scope> {
    return this.http.post<Scope>(`${this.base(workspaceId)}/${scopeId}/approve`, request);
  }

  revoke(workspaceId: string, scopeId: string, request: RevokeScopeRequest): Observable<Scope> {
    return this.http.post<Scope>(`${this.base(workspaceId)}/${scopeId}/revoke`, request);
  }

  expire(workspaceId: string, scopeId: string): Observable<Scope> {
    return this.http.post<Scope>(`${this.base(workspaceId)}/${scopeId}/expire`, {});
  }

  updateRestrictions(
    workspaceId: string,
    scopeId: string,
    request: UpdateScopeRestrictionsRequest,
  ): Observable<Scope> {
    return this.http.patch<Scope>(`${this.base(workspaceId)}/${scopeId}/restrictions`, request);
  }

  /** Evaluates a destination against approved scopes. Performs no network connection. */
  evaluate(workspaceId: string, request: EvaluateScopeRequest): Observable<ScopeEvaluation> {
    return this.http.post<ScopeEvaluation>(`${this.base(workspaceId)}/evaluate`, request);
  }
}

/** Administration queries available to global Administrators. */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  users(page = 1, pageSize = 25): Observable<unknown> {
    return this.http.get(apiUrl('/admin/users'), { params: paging(page, pageSize) });
  }

  audit(page = 1, pageSize = 25): Observable<unknown> {
    return this.http.get(apiUrl('/admin/audit'), { params: paging(page, pageSize) });
  }
}

function paging(page: number, pageSize: number): HttpParams {
  return new HttpParams().set('page', page).set('pageSize', pageSize);
}
