import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, input, isDevMode, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { EMPTY, Subject, catchError, finalize, merge, of, startWith, switchMap, timer } from 'rxjs';
import * as QRCode from 'qrcode';
import { AuthService } from '../auth/services/auth.service';
import { ReservationRealtimeService } from '../reservations/services/reservation-realtime.service';

export interface Invoice {
  id: string;
  reservationCode: string | null;
  totalKwh: number;
  kwhRate: number;
  swapRate: number | null;
  totalAmount: number;
  status: string;
  paymentReference: string | null;
  paymentMethod: 'Cash' | 'MockQr' | null;
  collectedByOperatorId: string | null;
  dueAtUtc: string | null;
  paidAtUtc: string | null;
  createdAtUtc?: string | null;
  customerName?: string | null;
  customerPhone?: string | null;
  customerEmail?: string | null;
  collectorName?: string | null;
}

export interface QrAttempt {
  id: string;
  invoiceId: string;
  qrPayload: string;
}

export type InvoiceFilterTab = 'all' | 'pending' | 'paid';

@Component({
  selector: 'app-invoices',
  imports: [CommonModule, DatePipe, DecimalPipe, FormsModule, RouterLink],
  templateUrl: './invoices.component.html',
  styleUrl: './invoices.component.scss',
})
export class InvoicesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly realtime = inject(ReservationRealtimeService);
  private readonly reload = new Subject<void>();
  private readonly rows = signal<Invoice[]>([]);

  readonly auth = inject(AuthService);
  protected readonly isDev = isDevMode();
  readonly stationMode = input(false);
  readonly focusInvoiceId = input<string | null>(null);
  private readonly focusChanges = toObservable(this.focusInvoiceId);
  private readonly focusedInvoice = signal<Invoice | null>(null);

  /* UI Filters & Search */
  readonly activeTab = signal<InvoiceFilterTab>('all');
  readonly searchQuery = signal('');
  readonly copiedCode = signal<string | null>(null);

  /* Ordered rows prioritizing focusInvoiceId if present */
  readonly invoices = computed(() => {
    const focus = this.focusInvoiceId();
    const items = this.rows();
    if (!focus) return items;
    const fallback = this.focusedInvoice();
    const invoice = items.find((row) => row.id === focus) ?? (fallback?.id === focus ? fallback : null);
    return invoice ? [invoice, ...items.filter((row) => row.id !== focus)] : items;
  });

  /* Filtered and searched invoices for display */
  readonly filteredInvoices = computed(() => {
    const tab = this.activeTab();
    const query = this.searchQuery().trim().toLowerCase();
    let list = this.invoices();

    if (tab === 'pending') {
      list = list.filter((inv) => inv.status === 'Pending');
    } else if (tab === 'paid') {
      list = list.filter((inv) => inv.status === 'Paid');
    }

    if (query) {
      list = list.filter((inv) =>
        (inv.reservationCode && inv.reservationCode.toLowerCase().includes(query)) ||
        (inv.customerName && inv.customerName.toLowerCase().includes(query)) ||
        (inv.customerPhone && inv.customerPhone.toLowerCase().includes(query)) ||
        (inv.collectorName && inv.collectorName.toLowerCase().includes(query)) ||
        inv.id.toLowerCase().includes(query)
      );
    }
    return list;
  });

  /* Financial and Operational KPI Statistics */
  readonly totalPaidAmount = computed(() => {
    return this.rows()
      .filter((inv) => inv.status === 'Paid')
      .reduce((sum, inv) => sum + (inv.totalAmount || 0), 0);
  });

  readonly totalPendingAmount = computed(() => {
    return this.rows()
      .filter((inv) => inv.status === 'Pending')
      .reduce((sum, inv) => sum + (inv.totalAmount || 0), 0);
  });

  readonly totalKwhCharged = computed(() => {
    return this.rows()
      .reduce((sum, inv) => sum + (inv.totalKwh || 0), 0);
  });

  readonly paidCount = computed(() => this.rows().filter((inv) => inv.status === 'Paid').length);
  readonly pendingCount = computed(() => this.rows().filter((inv) => inv.status === 'Pending').length);

  readonly attempt = signal<(QrAttempt & { image: string }) | null>(null);
  readonly activeInvoiceForQr = computed(() => {
    const att = this.attempt();
    if (!att) return null;
    return this.rows().find((r) => r.id === att.invoiceId) || null;
  });

  readonly busy = signal(false);
  readonly loaded = signal(false);
  readonly page = signal(1);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);

  ngOnInit(): void {
    this.focusChanges.pipe(
      switchMap((id) => this.stationMode() && id ? this.http.get<Invoice>(`/api/invoices/${encodeURIComponent(id)}`).pipe(
        catchError(() => of(null)),
      ) : of(null)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((invoice) => {
      this.focusedInvoice.set(invoice);
      if (invoice) {
        this.activeTab.set('all');
        this.searchQuery.set('');
      }
    });
    merge(this.reload, this.realtime.invoiceChanged$, timer(15000, 15000)).pipe(
      startWith(undefined),
      switchMap(() => this.auth.isAuthenticated() ? this.http.get<Invoice[]>('/api/invoices', { params: { page: this.page() } }).pipe(
        catchError(() => {
          this.error.set('Không tải được hóa đơn.');
          return EMPTY;
        }),
      ) : EMPTY),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((rows) => {
      this.rows.set(rows);
      this.loaded.set(true);
      const current = this.attempt();
      if (current && !rows.some((row) => row.id === current.invoiceId && row.status === 'Pending')) {
        this.attempt.set(null);
      }
    });
  }

  setFilter(tab: InvoiceFilterTab): void {
    this.activeTab.set(tab);
  }

  goToPage(page: number): void {
    if (page < 1) return;
    this.page.set(page);
    this.refresh();
  }

  refresh(): void {
    this.error.set(null);
    this.reload.next();
  }

  copyCode(code: string): void {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(code).then(() => {
        this.copiedCode.set(code);
        setTimeout(() => this.copiedCode.set(null), 2000);
      }).catch(() => {});
    }
  }

  closeQrModal(): void {
    this.attempt.set(null);
  }

  begin(invoiceId: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.http.post<QrAttempt>(`/api/invoices/${invoiceId}/pay`, {}).pipe(
      switchMap(async (attempt) => ({ ...attempt, image: await QRCode.toDataURL(attempt.qrPayload) })),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.busy.set(false)),
    ).subscribe({
      next: (attempt) => this.attempt.set(attempt),
      error: (err: HttpErrorResponse) => this.error.set(err.error?.detail || 'Không tạo được mã QR.'),
    });
  }

  settle(succeeded: boolean, amount?: number): void {
    const attempt = this.attempt();
    if (!attempt) return;
    this.submit(`/api/payments/${attempt.id}/mock-result`, { succeeded, amount },
      succeeded ? undefined : 'Lượt thử thất bại. Bạn có thể tạo lại mã QR.');
  }

  update(invoice: Invoice, action: 'cash' | 'defer'): void {
    const amount = new Intl.NumberFormat('vi-VN').format(invoice.totalAmount);
    const operatorName = this.auth.currentUser()?.fullName || this.auth.currentUser()?.email || 'Nhân viên trạm';
    const customerLabel = invoice.customerName || invoice.reservationCode || 'khách hàng';
    const prompt = action === 'cash'
      ? `Xác nhận nhân viên ${operatorName} đã thu đủ ${amount} đ tiền mặt từ khách hàng ${customerLabel}?`
      : `Xác nhận cho khách hàng ${customerLabel} trả sau ${amount} đ trong vòng 7 ngày?`;
    if (confirm(prompt)) this.submit(`/api/operator/me/invoices/${invoice.id}/${action}`, {});
  }

  private submit(url: string, body: object, notice?: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.http.post<void>(url, body).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.busy.set(false)),
    ).subscribe({
      next: () => {
        this.attempt.set(null);
        this.notice.set(notice ?? null);
        this.refresh();
      },
      error: (err: HttpErrorResponse) => {
        if (err.status === 409) this.attempt.set(null);
        this.notice.set(null);
        this.error.set(err.error?.detail || 'Không thể cập nhật thanh toán.');
        this.reload.next();
      },
    });
  }
}
