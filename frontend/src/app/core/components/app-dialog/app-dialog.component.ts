import { ChangeDetectionStrategy, Component, HostListener, inject } from '@angular/core';
import { DialogService } from '../../services/dialog.service';

@Component({
  selector: 'app-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (dialog.current(); as current) {
      <div class="dialog-backdrop" (click)="onBackdropClick()">
        <div
          class="dialog-card"
          [class]="'dialog-card tone-' + current.tone"
          [attr.role]="current.kind === 'alert' || current.tone === 'danger' ? 'alertdialog' : 'dialog'"
          aria-modal="true"
          aria-labelledby="app-dialog-title"
          aria-describedby="app-dialog-message"
          (click)="$event.stopPropagation()"
        >
          <div class="dialog-header">
            <div class="dialog-icon" aria-hidden="true">
              @switch (current.tone) {
                @case ('error') {
                  <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                    <circle cx="12" cy="12" r="10"></circle>
                    <line x1="15" y1="9" x2="9" y2="15"></line>
                    <line x1="9" y1="9" x2="15" y2="15"></line>
                  </svg>
                }
                @case ('danger') {
                  <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                    <line x1="12" y1="9" x2="12" y2="13"></line>
                    <line x1="12" y1="17" x2="12.01" y2="17"></line>
                  </svg>
                }
                @case ('warning') {
                  <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                    <line x1="12" y1="9" x2="12" y2="13"></line>
                    <line x1="12" y1="17" x2="12.01" y2="17"></line>
                  </svg>
                }
                @case ('success') {
                  <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path>
                    <polyline points="22 4 12 14.01 9 11.01"></polyline>
                  </svg>
                }
                @default {
                  <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                    <circle cx="12" cy="12" r="10"></circle>
                    <line x1="12" y1="16" x2="12" y2="12"></line>
                    <line x1="12" y1="8" x2="12.01" y2="8"></line>
                  </svg>
                }
              }
            </div>
            <div class="dialog-copy">
              <h2 id="app-dialog-title" class="dialog-title">{{ current.title }}</h2>
              <p id="app-dialog-message" class="dialog-message">{{ current.message }}</p>
            </div>
            <button
              type="button"
              class="dialog-close"
              aria-label="Đóng hộp thoại"
              (click)="dialog.close(false)"
            >
              ✕
            </button>
          </div>

          <div class="dialog-actions">
            @if (current.kind === 'confirm') {
              <button type="button" class="btn-secondary" (click)="dialog.close(false)">
                {{ current.cancelText }}
              </button>
            }
            <button
              type="button"
              class="btn-primary"
              autofocus
              (click)="dialog.close(true)"
            >
              {{ current.confirmText }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: `
    .dialog-backdrop {
      position: fixed;
      inset: 0;
      z-index: 9999;
      display: grid;
      place-items: center;
      padding: 16px;
      background: rgba(15, 23, 42, 0.52);
      backdrop-filter: blur(4px);
      animation: dialog-fade-in 0.14s ease-out;
    }
    .dialog-card {
      width: min(440px, 100%);
      background: #ffffff;
      border-radius: 16px;
      border: 1px solid #e2e8f0;
      box-shadow: 0 24px 60px rgba(15, 23, 42, 0.24);
      padding: 22px;
      display: flex;
      flex-direction: column;
      gap: 20px;
      animation: dialog-pop-in 0.16s cubic-bezier(0.16, 1, 0.3, 1);
    }
    .dialog-header {
      display: flex;
      align-items: flex-start;
      gap: 14px;
    }
    .dialog-icon {
      flex: none;
      width: 42px;
      height: 42px;
      border-radius: 12px;
      display: grid;
      place-items: center;
      background: #eaf5ed;
      color: #17634f;
    }
    .tone-error .dialog-icon,
    .tone-danger .dialog-icon {
      background: #fef2f2;
      color: #dc2626;
    }
    .tone-warning .dialog-icon {
      background: #fffbeb;
      color: #d97706;
    }
    .tone-info .dialog-icon {
      background: #eff6ff;
      color: #2563eb;
    }
    .dialog-copy {
      flex: 1;
      min-width: 0;
    }
    .dialog-title {
      margin: 0 0 6px;
      font-size: 16px;
      font-weight: 700;
      color: #0f172a;
      line-height: 1.35;
    }
    .dialog-message {
      margin: 0;
      font-size: 14px;
      color: #475569;
      line-height: 1.55;
      white-space: pre-line;
      word-break: break-word;
    }
    .dialog-close {
      flex: none;
      width: 30px;
      height: 30px;
      border: 0;
      border-radius: 8px;
      background: transparent;
      color: #64748b;
      cursor: pointer;
      font-size: 14px;
      display: grid;
      place-items: center;
      transition: background 0.15s ease, color 0.15s ease;
    }
    .dialog-close:hover {
      background: #f1f5f9;
      color: #0f172a;
    }
    .dialog-actions {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
    }
    .btn-secondary,
    .btn-primary {
      height: 40px;
      padding: 0 18px;
      border-radius: 10px;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.15s ease;
      font-family: inherit;
    }
    .btn-secondary {
      border: 1px solid #cbd5e1;
      background: #ffffff;
      color: #334155;
    }
    .btn-secondary:hover {
      background: #f8fafc;
      border-color: #94a3b8;
    }
    .btn-primary {
      border: 0;
      background: #17634f;
      color: #ffffff;
    }
    .btn-primary:hover {
      background: #124e3e;
    }
    .tone-error .btn-primary,
    .tone-danger .btn-primary {
      background: #dc2626;
    }
    .tone-error .btn-primary:hover,
    .tone-danger .btn-primary:hover {
      background: #b91c1c;
    }
    .tone-warning .btn-primary {
      background: #d97706;
    }
    .tone-warning .btn-primary:hover {
      background: #b45309;
    }
    @keyframes dialog-fade-in {
      from { opacity: 0; }
      to { opacity: 1; }
    }
    @keyframes dialog-pop-in {
      from { opacity: 0; transform: scale(0.96) translateY(6px); }
      to { opacity: 1; transform: scale(1) translateY(0); }
    }
  `,
})
export class AppDialogComponent {
  protected readonly dialog = inject(DialogService);

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.dialog.current()) {
      this.dialog.close(false);
    }
  }

  onBackdropClick(): void {
    this.dialog.close(false);
  }
}
