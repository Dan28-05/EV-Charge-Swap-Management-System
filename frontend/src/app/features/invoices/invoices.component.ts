import { DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../auth/services/auth.service';

interface Invoice {
  id: string;
  totalKwh: number;
  kwhRate: number;
  swapRate: number | null;
  totalAmount: number;
  status: string;
  paymentReference: string | null;
}

@Component({
  selector: 'app-invoices',
  imports: [DecimalPipe, RouterLink],
  template: `
    <main>
      <a routerLink="/">Quay lại bản đồ trạm</a>
      <h1>Hóa đơn</h1>
      @if (!auth.isAuthenticated()) {
        <p><a routerLink="/login">Đăng nhập</a> để xem hóa đơn.</p>
      } @else {
        <button type="button" (click)="refresh()" [disabled]="busy()">Làm mới</button>
        @if (message(); as text) { <p role="status">{{ text }}</p> }
        @if (error(); as text) { <p role="alert">{{ text }}</p> }
        @for (invoice of invoices(); track invoice.id) {
          <article>
            <h2>{{ invoice.swapRate == null ? 'Sạc ô tô' : 'Đổi pin' }}</h2>
            @if (invoice.swapRate == null) {
              <p>{{ invoice.totalKwh | number:'1.0-3' }} kWh × {{ invoice.kwhRate | number }} đ/kWh</p>
            } @else {
              <p>1 lượt × {{ invoice.swapRate | number }} đ/lượt</p>
            }
            <p><strong>{{ invoice.totalAmount | number }} đ</strong> ·
              {{ invoice.status === 'Paid' ? 'Đã thanh toán' : invoice.status === 'Pending' ? 'Chờ thanh toán' : 'Đã hủy' }}</p>
            @if (invoice.paymentReference) { <p>Mã thanh toán: {{ invoice.paymentReference }}</p> }
            @if (invoice.status === 'Pending' && auth.currentUser()?.roles?.includes('Customer')) {
              @if (attempt()?.invoiceId === invoice.id) {
                <div class="actions">
                  <button type="button" [disabled]="busy()" (click)="settle(true)">Giả lập thành công</button>
                  <button type="button" [disabled]="busy()" (click)="settle(false)">Giả lập thất bại</button>
                </div>
              } @else {
                <button type="button" [disabled]="busy()" (click)="begin(invoice.id)">Thanh toán thử</button>
              }
            }
          </article>
        } @empty {
          @if (!busy() && !error()) { <p>Chưa có hóa đơn.</p> }
        }
      }
    </main>
  `,
  styles: `
    main { max-width: 48rem; margin-inline: auto; padding: 1.5rem; }
    article { margin-top: 1rem; padding: 1rem; border: 1px solid #dbe3ed; border-radius: 0.75rem; }
    h2 { margin-top: 0; font-size: 1.1rem; }
    .actions { display: flex; flex-wrap: wrap; gap: 0.5rem; }
  `,
})
export class InvoicesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly invoices = signal<Invoice[]>([]);
  readonly attempt = signal<{ id: string; invoiceId: string } | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  ngOnInit(): void { this.refresh(); }

  refresh(): void {
    if (!this.auth.isAuthenticated() || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.http.get<Invoice[]>('/api/invoices').pipe(
      takeUntilDestroyed(this.destroyRef), finalize(() => this.busy.set(false)),
    ).subscribe({
      next: (invoices) => this.invoices.set(invoices),
      error: () => this.error.set('Không tải được hóa đơn.'),
    });
  }

  begin(invoiceId: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    this.http.post<{ id: string }>(`/api/invoices/${invoiceId}/pay`, {}).pipe(
      takeUntilDestroyed(this.destroyRef), finalize(() => this.busy.set(false)),
    ).subscribe({
      next: ({ id }) => this.attempt.set({ id, invoiceId }),
      error: (err) => this.error.set(err.status === 503 ? 'Thanh toán thử chưa được cấu hình.' : 'Không tạo được lượt thanh toán.'),
    });
  }

  settle(succeeded: boolean): void {
    const attempt = this.attempt();
    if (!attempt || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.http.post<void>(`/api/payments/${attempt.id}/mock-result`, { succeeded }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.attempt.set(null);
        this.message.set(succeeded ? 'Đã xác nhận thanh toán thử.' : 'Lượt thanh toán thất bại. Bạn có thể thử lại.');
        this.busy.set(false);
        this.refresh();
      },
      error: () => {
        this.busy.set(false);
        this.error.set('Không xác nhận được kết quả thanh toán.');
      },
    });
  }
}
