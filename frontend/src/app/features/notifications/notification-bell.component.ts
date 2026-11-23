import { CommonModule, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { EMPTY, Subject, auditTime, catchError, finalize, merge, switchMap, timer } from 'rxjs';
import { RealtimeNotification, ReservationRealtimeService } from '../reservations/services/reservation-realtime.service';

interface Inbox {
  items: RealtimeNotification[];
  unreadCount: number;
}

@Component({
  selector: 'app-notification-bell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, DatePipe],
  template: `
    <!-- BELL BUTTON TRIGGER -->
    <button #bell type="button" class="bell-btn"
      [attr.aria-expanded]="isOpen()"
      [attr.aria-label]="'Thông báo, ' + inbox().unreadCount + ' chưa đọc'"
      [class.has-unread]="inbox().unreadCount > 0"
      [class.active]="isOpen()"
      (click)="toggle()"
      [title]="heading()">
      <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <path d="M18 8a6 6 0 0 0-12 0v4c0 2-2 3-2 4h16c0-1-2-2-2-4V8" />
        <path d="M10 20h4" />
      </svg>
      @if (inbox().unreadCount > 0) {
        <span class="unread-badge" aria-hidden="true">
          {{ inbox().unreadCount > 99 ? '99+' : inbox().unreadCount }}
        </span>
      }
    </button>

    <span class="sr-only" role="status" aria-live="polite">{{ preview()?.title }}</span>

    <!-- DYNAMIC ISLAND / TOAST PREVIEW -->
    @if (preview(); as current) {
      @for (item of [current]; track item.id) {
        <div #island class="island" [class.folding]="foldTarget() !== null"
          [class.island-warning]="getNotificationCategory(item) === 'warning'"
          [class.island-payment]="getNotificationCategory(item) === 'payment'"
          [style.--fold-x]="foldTarget()?.x" [style.--fold-y]="foldTarget()?.y"
          (animationend)="finishFold($event)">
          <button type="button" class="island-content" [disabled]="saving() || foldTarget() !== null"
            (click)="read(item, true)">
            <span class="island-icon" aria-hidden="true">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none"
                stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                @switch (getNotificationCategory(item)) {
                  @case ('warning') {
                    <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                    <line x1="12" y1="9" x2="12" y2="13"></line>
                    <line x1="12" y1="17" x2="12.01" y2="17"></line>
                  }
                  @case ('payment') {
                    <rect x="2" y="4" width="20" height="16" rx="2"></rect>
                    <line x1="2" y1="10" x2="22" y2="10"></line>
                  }
                  @case ('charging') {
                    <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>
                  }
                  @default {
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                    <polyline points="14 2 14 8 20 8"></polyline>
                  }
                }
              </svg>
            </span>
            <span class="island-copy">
              <strong>{{ item.title }}</strong>
              <span>{{ item.message }}</span>
            </span>
          </button>
          <button type="button" class="island-dismiss" aria-label="Thu thông báo về chuông"
            [disabled]="foldTarget() !== null" (click)="foldPreview()" title="Đóng thông báo">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor"
              stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>
      }
    }

    <!-- POPOVER BACKDROP (CLOSES ON ANY CLICK OUTSIDE) -->
    @if (isOpen()) {
      <div class="panel-backdrop" (click)="close()" aria-hidden="true"></div>

      <!-- NOTIFICATION FLOATING PANEL -->
      <div class="notification-panel" role="dialog" aria-modal="true" aria-labelledby="notification-bell-title">
        <!-- PANEL HEADER -->
        <header class="panel-header">
          <div class="header-title-wrap">
            <div class="title-with-badge">
              <h2 id="notification-bell-title">{{ heading() }}</h2>
              @if (inbox().unreadCount > 0) {
                <span class="unread-pill">{{ inbox().unreadCount }} mới</span>
              }
            </div>
          </div>

          <div class="header-actions">
            @if (inbox().unreadCount > 0) {
              <button type="button" class="btn-mark-all" [disabled]="saving()" (click)="markAll()" title="Đánh dấu tất cả là đã đọc">
                <svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                  <polyline points="20 6 9 17 4 12"></polyline>
                </svg>
                <span>Đã đọc hết</span>
              </button>
            }
            <button type="button" class="btn-close-panel" aria-label="Đóng thông báo (Esc)" (click)="close()" title="Đóng (Esc)">
              <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>
        </header>

        <!-- FILTER TABS -->
        <div class="panel-tabs" role="tablist" aria-label="Lọc thông báo">
          <button type="button" role="tab" [attr.aria-selected]="activeFilter() === 'all'" class="tab-btn" [class.active]="activeFilter() === 'all'" (click)="activeFilter.set('all')">
            <span>Tất cả</span>
            <span class="tab-count">{{ inbox().items.length }}</span>
          </button>
          <button type="button" role="tab" [attr.aria-selected]="activeFilter() === 'unread'" class="tab-btn" [class.active]="activeFilter() === 'unread'" (click)="activeFilter.set('unread')">
            <span>Chưa đọc</span>
            @if (inbox().unreadCount > 0) {
              <span class="tab-count badge-unread">{{ inbox().unreadCount }}</span>
            }
          </button>
        </div>

        <!-- ERROR FEEDBACK -->
        @if (loadError()) {
          <div class="feedback-banner error" role="alert">
            <p>Không tải được thông báo.</p>
            <button type="button" class="btn-retry" (click)="reload()">Thử lại</button>
          </div>
        }
        @if (writeError()) {
          <div class="feedback-banner error" role="alert">
            <p>Không cập nhật được trạng thái. Vui lòng thử lại.</p>
          </div>
        }

        <!-- NOTIFICATIONS LIST -->
        <div class="notification-scroll-area">
          @if (filteredItems().length > 0) {
            <ul class="notification-list">
              @for (item of filteredItems(); track item.id) {
                <li class="notification-item-wrap">
                  <button type="button" class="notification-card" [class.unread]="!item.isRead"
                    [disabled]="saving()" (click)="read(item)">
                    <div class="item-icon-box" [ngClass]="getNotificationCategory(item)">
                      @switch (getNotificationCategory(item)) {
                        @case ('warning') {
                          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                            <line x1="12" y1="9" x2="12" y2="13"></line>
                            <line x1="12" y1="17" x2="12.01" y2="17"></line>
                          </svg>
                        }
                        @case ('payment') {
                          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                            <rect x="2" y="4" width="20" height="16" rx="2"></rect>
                            <line x1="2" y1="10" x2="22" y2="10"></line>
                          </svg>
                        }
                        @case ('charging') {
                          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                            <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>
                          </svg>
                        }
                        @default {
                          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                            <polyline points="14 2 14 8 20 8"></polyline>
                          </svg>
                        }
                      }
                    </div>

                    <div class="item-body">
                      <div class="item-top">
                        <span class="item-title">{{ item.title }}</span>
                        @if (!item.isRead) {
                          <span class="unread-dot" title="Chưa đọc"></span>
                        }
                      </div>
                      <p class="item-msg">{{ item.message }}</p>
                      <div class="item-footer">
                        <time [attr.datetime]="item.createdAtUtc" class="item-time">
                          {{ getRelativeTime(item.createdAtUtc) || (item.createdAtUtc | date:'HH:mm · dd/MM':'+0700') }}
                        </time>
                        @if (item.actionUrl) {
                          <span class="item-action-link">Chi tiết ↗</span>
                        }
                      </div>
                    </div>
                  </button>
                </li>
              }
            </ul>
          } @else {
            <!-- EMPTY STATE -->
            <div class="empty-state">
              <div class="empty-icon-wrap">
                <svg viewBox="0 0 24 24" width="28" height="28" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"></path>
                  <path d="M13.73 21a2 2 0 0 1-3.46 0"></path>
                  <line x1="1" y1="1" x2="23" y2="23"></line>
                </svg>
              </div>
              <h4>Không có thông báo nào</h4>
              <p>{{ activeFilter() === 'unread' ? 'Bạn đã đọc toàn bộ thông báo.' : 'Thông báo mới sẽ xuất hiện tại đây.' }}</p>
            </div>
          }
        </div>

        <!-- PANEL FOOTER -->
        <footer class="panel-footer">
          <button type="button" class="btn-all-history" (click)="openHistory()">
            <span>Xem tất cả thông báo hệ thống</span>
            <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <line x1="5" y1="12" x2="19" y2="12"></line>
              <polyline points="12 5 19 12 12 19"></polyline>
            </svg>
          </button>
        </footer>
      </div>
    }
  `,
  styleUrl: './notification-bell.component.scss',
})
export class NotificationBellComponent {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly realtime = inject(ReservationRealtimeService);
  private readonly refresh = new Subject<void>();
  private readonly bell = viewChild.required<ElementRef<HTMLButtonElement>>('bell');
  private readonly island = viewChild<ElementRef<HTMLElement>>('island');
  private readonly pending: RealtimeNotification[] = [];
  private readonly seen = new Set<string>();
  private previewTimer?: ReturnType<typeof setTimeout>;

  readonly audience = input.required<'operator' | 'customer'>();
  readonly reservationRequested = output<void>();
  readonly heading = computed(() => this.audience() === 'operator' ? 'Thông báo trạm' : 'Thông báo');
  readonly isOpen = signal(false);
  readonly preview = signal<RealtimeNotification | null>(null);
  readonly foldTarget = signal<{ x: string; y: string } | null>(null);
  readonly inbox = signal<Inbox>({ items: [], unreadCount: 0 });
  readonly loaded = signal(false);
  readonly loadError = signal(false);
  readonly writeError = signal(false);
  readonly saving = signal(false);

  readonly activeFilter = signal<'all' | 'unread'>('all');
  readonly filteredItems = computed(() => {
    const items = this.inbox().items;
    if (this.activeFilter() === 'unread') {
      return items.filter((item) => !item.isRead);
    }
    return items;
  });

  constructor() {
    this.realtime.receivedNotification$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((item) => {
      if (item.isRead || !this.isForAudience(item) || this.seen.has(item.id)) return;
      this.seen.add(item.id);
      if (this.seen.size > 128) {
        const oldest = this.seen.values().next().value;
        if (oldest) this.seen.delete(oldest);
      }
      if (this.pending.length === 8) this.pending.shift();
      this.pending.push(item);
      this.showNextPreview();
    });
    this.destroyRef.onDestroy(() => clearTimeout(this.previewTimer));
    merge(timer(0, 20000), this.refresh, this.realtime.notificationsChanged$).pipe(
      auditTime(100),
      switchMap(() => this.http.get<Inbox>('/api/notifications').pipe(catchError(() => {
        this.loadError.set(true);
        return EMPTY;
      }))),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((inbox) => {
      this.inbox.set(inbox);
      this.loaded.set(true);
      this.loadError.set(false);
    });
  }

  toggle(): void {
    if (this.isOpen()) {
      this.close();
    } else {
      this.open();
    }
  }

  open(): void {
    this.writeError.set(false);
    this.isOpen.set(true);
    this.clearPreview();
    this.reload();
  }

  close(): void {
    if (!this.isOpen()) return;
    this.isOpen.set(false);
    this.showNextPreview();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.isOpen()) {
      this.close();
    }
  }

  openHistory(): void {
    this.close();
    void this.router.navigate(['/notifications']);
  }

  reload(): void { this.refresh.next(); }

  read(item: RealtimeNotification, fromPreview = false): void {
    const navigate = () => {
      this.close();
      if (fromPreview) this.foldPreview();
      if (this.audience() === 'operator') {
        if (item.actionUrl?.startsWith('/operator/dashboard?')) void this.router.navigateByUrl(item.actionUrl);
      } else if (item.actionUrl === '/reservations') {
        this.reservationRequested.emit();
      } else {
        void this.router.navigate([item.actionUrl === '/invoices' ? '/invoices' : '/notifications']);
      }
    };
    if (item.isRead) {
      navigate();
    } else {
      this.mark(`/api/notifications/${item.id}/read`, navigate, fromPreview ? () => {
        this.open();
        this.writeError.set(true);
      } : undefined);
    }
  }

  private isForAudience(item: RealtimeNotification): boolean {
    const operatorItem = item.actionUrl?.startsWith('/operator/dashboard?') === true;
    return this.audience() === 'operator' ? operatorItem : !operatorItem;
  }

  markAll(): void { this.mark('/api/notifications/read-all', () => this.pending.splice(0)); }

  showNextPreview(): void {
    if (this.preview() || !this.pending.length || this.isOpen()) return;
    const item = this.pending.shift();
    if (!item) return;
    this.preview.set(item);
    this.previewTimer = setTimeout(() => this.dismissWhenIdle(), 5000);
  }

  foldPreview(): void {
    const island = this.island()?.nativeElement;
    if (!island || this.foldTarget()) return;
    clearTimeout(this.previewTimer);
    if (island.contains(island.ownerDocument.activeElement)) this.bell().nativeElement.focus();
    const source = island.getBoundingClientRect();
    const target = this.bell().nativeElement.getBoundingClientRect();
    this.foldTarget.set({
      x: `${target.left + target.width / 2 - source.left - source.width / 2}px`,
      y: `${target.top + target.height / 2 - source.top - source.height / 2}px`,
    });
    this.previewTimer = setTimeout(() => this.advancePreview(), 560);
  }

  finishFold(event: AnimationEvent): void {
    if (event.target === event.currentTarget && event.animationName === 'island-fold') this.advancePreview();
  }

  getNotificationCategory(item: RealtimeNotification): 'warning' | 'payment' | 'charging' | 'checkin' {
    const text = (item.title + ' ' + item.message).toLowerCase();
    if (text.includes('sự cố') || text.includes('hỏng') || text.includes('lỗi')) {
      return 'warning';
    }
    if (text.includes('thanh toán') || text.includes('hóa đơn') || text.includes('tiền')) {
      return 'payment';
    }
    if (item.type === 'Warning') return 'warning';
    if (text.includes('sạc') || text.includes('pin') || text.includes('kwh') || text.includes('trụ sạc')) {
      return 'charging';
    }
    return 'checkin';
  }

  getRelativeTime(dateStr: string): string {
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / (1000 * 60));
    const diffHours = Math.floor(diffMins / 60);

    if (diffMins < 1) return 'Vừa xong';
    if (diffMins < 60) return `${diffMins} phút trước`;
    if (diffHours < 24) return `${diffHours} giờ trước`;
    return '';
  }

  private advancePreview(): void {
    this.clearPreview();
    this.showNextPreview();
  }

  private dismissWhenIdle(): void {
    const island = this.island()?.nativeElement;
    if (island?.matches(':hover, :focus-within') || this.saving()) {
      this.previewTimer = setTimeout(() => this.dismissWhenIdle(), 1000);
      return;
    }
    this.foldPreview();
  }

  private clearPreview(): void {
    clearTimeout(this.previewTimer);
    this.preview.set(null);
    this.foldTarget.set(null);
  }

  private mark(url: string, afterSave?: () => void, afterError?: () => void): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.writeError.set(false);
    this.http.put<void>(url, {}).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.saving.set(false)),
    ).subscribe({
      next: () => {
        this.reload();
        afterSave?.();
      },
      error: () => {
        this.writeError.set(true);
        afterError?.();
      },
    });
  }
}
