import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'info';

export interface Toast {
  id: number;
  kind: ToastKind;
  text: string;
}

/** Transient operator feedback. Signal-backed so it renders correctly under zoneless change detection. */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private sequence = 0;
  private readonly items = signal<Toast[]>([]);

  readonly toasts = this.items.asReadonly();

  success(text: string): void {
    this.push('success', text);
  }

  error(text: string): void {
    this.push('error', text);
  }

  info(text: string): void {
    this.push('info', text);
  }

  dismiss(id: number): void {
    this.items.update((current) => current.filter((toast) => toast.id !== id));
  }

  private push(kind: ToastKind, text: string): void {
    const id = ++this.sequence;
    this.items.update((current) => [...current, { id, kind, text }]);

    const ttl = kind === 'error' ? 9000 : 4500;
    setTimeout(() => this.dismiss(id), ttl);
  }
}
