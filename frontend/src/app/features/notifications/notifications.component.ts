import { CommonModule, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../auth/services/auth.service';

export interface NotificationItem {
  id: string;
  title: string;
  message: string;
  createdAtUtc: string;
  isRead?: boolean;
  actionUrl?: string | null;
}

export type NotificationCategory = 'all' | 'booking' | 'charging' | 'payment';

@Component({
  selector: 'app-notifications',
  imports: [CommonModule, DatePipe, RouterLink],
  templateUrl: './notifications.component.html',
  styleUrl: './notifications.component.scss',
})
export class NotificationsComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly auth = inject(AuthService);
  readonly items = signal<NotificationItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly unreadCount = signal(0);
  readonly pageSize = signal(20);
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));

  readonly activeCategory = signal<NotificationCategory>('all');
  readonly copiedCode = signal<string | null>(null);
  readonly saving = signal(false);

  /* Categorized and filtered items */
  readonly categorizedItems = computed(() => {
    return this.items().map((item) => ({
      ...item,
      category: this.classify(item),
      extractedCode: this.extractCode(item.message),
    }));
  });

  ngOnInit(): void {
    this.refresh();
  }

  setCategory(category: NotificationCategory): void {
    if (this.loading()) return;
    this.activeCategory.set(category);
    this.page.set(1);
    this.refresh();
  }

  goToPage(page: number): void {
    if (this.loading() || page < 1 || page > this.pageCount()) return;
    this.page.set(page);
    this.refresh();
  }

  onItemClick(item: NotificationItem): void {
    if (item.actionUrl?.startsWith('/operator/dashboard?')) {
      this.openOperatorNotification(item);
      return;
    }
    if (!item.isRead) {
      this.markRead(item.id);
    }
  }

  markRead(id: string): void {
    this.http.put<void>(`/api/notifications/${id}/read`, {}).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.items.update((list) => list.map((i) => (i.id === id ? { ...i, isRead: true } : i)));
        this.unreadCount.update((c) => Math.max(0, c - 1));
      },
      error: () => {},
    });
  }

  markAll(): void {
    if (this.saving() || this.unreadCount() === 0) return;
    this.saving.set(true);
    this.http.put<void>('/api/notifications/read-all', {}).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.saving.set(false)),
    ).subscribe({
      next: () => {
        this.items.update((list) => list.map((i) => ({ ...i, isRead: true })));
        this.unreadCount.set(0);
      },
      error: () => this.error.set('Không thể đánh dấu đã đọc. Vui lòng thử lại.'),
    });
  }

  openOperatorNotification(item: NotificationItem): void {
    const url = item.actionUrl;
    if (!url?.startsWith('/operator/dashboard?')) return;
    if (item.isRead) {
      void this.router.navigateByUrl(url);
      return;
    }
    this.http.put<void>(`/api/notifications/${item.id}/read`, {}).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.items.update((list) => list.map((i) => (i.id === item.id ? { ...i, isRead: true } : i)));
        this.unreadCount.update((c) => Math.max(0, c - 1));
        void this.router.navigateByUrl(url);
      },
      error: () => this.error.set('Không thể đánh dấu đã đọc. Vui lòng thử lại.'),
    });
  }

  refresh(): void {
    if (this.loading() || !this.auth.isAuthenticated()) return;
    this.loading.set(true);
    this.error.set(null);
    const params: Record<string, string> = {};
    if (this.page() !== 1) params['page'] = String(this.page());
    if (this.activeCategory() !== 'all') params['category'] = this.activeCategory();
    this.http.get<{ items: NotificationItem[]; totalCount: number; pageSize: number; unreadCount?: number }>('/api/notifications', { params }).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false)),
    ).subscribe({
      next: ({ items, totalCount, pageSize, unreadCount }) => {
        this.items.set(items);
        this.total.set(totalCount ?? items.length);
        this.pageSize.set(pageSize ?? 20);
        if (typeof unreadCount === 'number') {
          this.unreadCount.set(unreadCount);
        } else {
          this.unreadCount.set(items.filter((i) => !i.isRead).length);
        }
      },
      error: () => this.error.set('Không tải được thông báo. Vui lòng thử lại sau.'),
    });
  }

  copyCode(code: string): void {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(code).then(() => {
        this.copiedCode.set(code);
        setTimeout(() => this.copiedCode.set(null), 2000);
      }).catch(() => {});
    }
  }

  classify(item: NotificationItem): 'booking' | 'charging' | 'payment' | 'warning' {
    const text = (item.title + ' ' + item.message).toLowerCase();
    if (text.includes('sự cố') || text.includes('hỏng') || text.includes('lỗi') || text.includes('cảnh báo')) {
      return 'warning';
    }
    if (text.includes('thanh toán') || text.includes('hóa đơn') || text.includes('tiền') || text.includes('qr')) {
      return 'payment';
    }
    if (text.includes('sạc') || text.includes('kwh') || text.includes('pin') || text.includes('trụ sạc')) {
      return 'charging';
    }
    return 'booking';
  }

  extractCode(message: string): string | null {
    const match = message.match(/[A-Fa-f0-9]{16,32}/);
    return match ? match[0] : null;
  }

  getRelativeTime(dateStr: string): string {
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / (1000 * 60));
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    if (diffMins < 1) return 'Vừa xong';
    if (diffMins < 60) return `${diffMins} phút trước`;
    if (diffHours < 24) return `${diffHours} giờ trước`;
    if (diffDays === 1) return 'Hôm qua';
    if (diffDays < 7) return `${diffDays} ngày trước`;
    return '';
  }
}
