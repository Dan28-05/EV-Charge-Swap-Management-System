import { Injectable, signal } from '@angular/core';

export type DialogTone = 'info' | 'success' | 'warning' | 'error' | 'primary' | 'danger';

export interface AlertDialogOptions {
  title?: string;
  tone?: DialogTone;
  confirmText?: string;
}

export interface ConfirmDialogOptions {
  title?: string;
  tone?: DialogTone;
  confirmText?: string;
  cancelText?: string;
}

export interface ActiveDialog {
  id: number;
  kind: 'alert' | 'confirm';
  title: string;
  message: string;
  tone: DialogTone;
  confirmText: string;
  cancelText: string;
}

interface QueuedDialog extends ActiveDialog {
  resolve: (confirmed: boolean) => void;
}

@Injectable({ providedIn: 'root' })
export class DialogService {
  private nextId = 1;
  private readonly queue: QueuedDialog[] = [];
  private currentEntry: QueuedDialog | null = null;

  readonly current = signal<ActiveDialog | null>(null);

  /** Opens an alert dialog and resolves when the user dismisses it. */
  alert(message: string, options: AlertDialogOptions = {}): Promise<void> {
    return this.enqueue({
      id: this.nextId++,
      kind: 'alert',
      title: options.title ?? 'Thông báo',
      message,
      tone: options.tone ?? 'error',
      confirmText: options.confirmText ?? 'Đã hiểu',
      cancelText: '',
    }).then(() => undefined);
  }

  /** Opens a confirmation dialog and resolves with true if confirmed, false if cancelled. */
  confirm(message: string, options: ConfirmDialogOptions = {}): Promise<boolean> {
    return this.enqueue({
      id: this.nextId++,
      kind: 'confirm',
      title: options.title ?? 'Xác nhận thao tác',
      message,
      tone: options.tone ?? 'primary',
      confirmText: options.confirmText ?? 'Xác nhận',
      cancelText: options.cancelText ?? 'Hủy',
    });
  }

  /** Closes the currently active dialog with the chosen result and shows the next queued dialog. */
  close(confirmed: boolean): void {
    const active = this.currentEntry;
    if (!active) return;
    this.currentEntry = null;
    this.current.set(null);
    active.resolve(confirmed);
    this.showNext();
  }

  private enqueue(dialog: ActiveDialog): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      this.queue.push({ ...dialog, resolve });
      if (!this.currentEntry) {
        this.showNext();
      }
    });
  }

  private showNext(): void {
    const next = this.queue.shift() ?? null;
    this.currentEntry = next;
    this.current.set(
      next
        ? {
            id: next.id,
            kind: next.kind,
            title: next.title,
            message: next.message,
            tone: next.tone,
            confirmText: next.confirmText,
            cancelText: next.cancelText,
          }
        : null,
    );
  }
}
