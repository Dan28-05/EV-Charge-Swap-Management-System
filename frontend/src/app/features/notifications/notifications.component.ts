import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../auth/services/auth.service';

interface NotificationItem {
  id: string;
  title: string;
  message: string;
  createdAtUtc: string;
}

@Component({
  selector: 'app-notifications',
  imports: [DatePipe, RouterLink],
  template: `
    <main class="notifications">
      <a routerLink="/">Quay lại bản đồ trạm</a>
      <h1>Thông báo</h1>
      @if (!auth.isAuthenticated()) {
        <p><a routerLink="/login">Đăng nhập</a> để xem thông báo của bạn.</p>
      } @else {
        <button type="button" [disabled]="loading()" (click)="refresh()">Làm mới</button>
        @if (error(); as message) { <p role="alert">{{ message }}</p> }
        @if (loading()) { <p role="status">Đang tải thông báo…</p> }
        @if (!loading() && !error() && items().length === 0) { <p>Bạn chưa có thông báo.</p> }
        @for (item of items(); track item.id) {
          <article>
            <h2>{{ item.title }}</h2>
            <time [attr.datetime]="item.createdAtUtc">{{ item.createdAtUtc | date:'dd/MM/yyyy HH:mm':'+0700' }}</time>
            <p>{{ item.message }}</p>
          </article>
        }
      }
    </main>
  `,
  styles: `
    .notifications { max-width: 48rem; margin-inline: auto; padding: 1.5rem; }
    article { margin-top: 1rem; padding: 1rem; border: 1px solid #dbe3ed; border-radius: 0.75rem; }
    h2 { margin-top: 0; font-size: 1.1rem; }
    time { color: #526174; font-size: 0.875rem; }
  `,
})
export class NotificationsComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly items = signal<NotificationItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    if (this.loading() || !this.auth.isAuthenticated()) return;
    this.loading.set(true);
    this.error.set(null);
    this.http.get<{ items: NotificationItem[] }>('/api/notifications').pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false)),
    ).subscribe({
      next: ({ items }) => this.items.set(items),
      error: () => this.error.set('Không tải được thông báo. Vui lòng thử lại.'),
    });
  }
}
