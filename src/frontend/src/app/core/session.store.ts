import { Injectable, computed, signal } from '@angular/core';
import { UserAccount } from './models';

/**
 * Holds the authenticated account in signals.
 *
 * Deliberately free of any HttpClient dependency so HTTP interceptors can inject it without
 * creating a dependency cycle. The API remains the only authority on what the account may do.
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly userState = signal<UserAccount | null>(null);
  private readonly loadedState = signal(false);

  readonly user = this.userState.asReadonly();
  readonly loaded = this.loadedState.asReadonly();
  readonly isAuthenticated = computed(() => this.userState() !== null);
  readonly roles = computed(() => this.userState()?.roles ?? []);
  readonly isGlobalAdministrator = computed(() => this.roles().includes('Administrator'));

  setUser(user: UserAccount | null): void {
    this.userState.set(user);
    this.loadedState.set(true);
  }

  markLoaded(): void {
    this.loadedState.set(true);
  }

  clear(): void {
    this.userState.set(null);
    this.loadedState.set(true);
  }
}
