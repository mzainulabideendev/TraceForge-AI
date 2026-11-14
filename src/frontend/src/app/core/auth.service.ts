import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { apiUrl } from './api-config';
import { SessionStore } from './session.store';
import { resetCsrfToken } from './csrf.interceptor';
import { UserAccount } from './models';

/** Authentication use cases. All failures propagate so callers can render the problem details. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  /** Restores the session from the API once. Never throws: an unreachable API means anonymous. */
  async restore(): Promise<void> {
    try {
      const user = await firstValueFrom(this.http.get<UserAccount>(apiUrl('/auth/me')));
      this.session.setUser(user);
    } catch {
      this.session.clear();
    }
  }

  async login(email: string, password: string): Promise<UserAccount> {
    const user = await firstValueFrom(
      this.http.post<UserAccount>(apiUrl('/auth/login'), { email, password }),
    );
    // The token issued while anonymous is bound to the anonymous identity; refresh it.
    resetCsrfToken();
    this.session.setUser(user);
    return user;
  }

  async register(email: string, password: string, displayName: string): Promise<UserAccount> {
    const user = await firstValueFrom(
      this.http.post<UserAccount>(apiUrl('/auth/register'), { email, password, displayName }),
    );
    resetCsrfToken();
    this.session.setUser(user);
    return user;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>(apiUrl('/auth/logout'), null));
    } finally {
      resetCsrfToken();
      this.session.clear();
    }
  }
}
