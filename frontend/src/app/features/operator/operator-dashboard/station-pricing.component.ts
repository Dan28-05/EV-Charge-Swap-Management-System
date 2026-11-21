import { DecimalPipe, registerLocaleData } from '@angular/common';
import localeVi from '@angular/common/locales/vi';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subject, catchError, finalize, merge, of, switchMap } from 'rxjs';
import { OperatorLocationInfo } from '../models/operator.models';
import { CurrentStationTariffs, OperatorService } from '../services/operator.service';

registerLocaleData(localeVi);

const services = [
  { id: 1, key: 'charging', label: 'Sạc ô tô', unit: 'kWh', maximum: 10000 },
  { id: 2, key: 'batterySwap', label: 'Đổi pin', unit: 'lượt', maximum: 999999 },
] as const;

@Component({
  selector: 'app-station-pricing',
  imports: [DecimalPipe, FormsModule],
  template: `
    <button type="button" class="price-trigger" aria-haspopup="dialog" (click)="open(editor)" title="Xem và điều chỉnh bảng giá tại trạm">
      <span class="price-trigger-icon" aria-hidden="true">
        <svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
          <line x1="12" y1="1" x2="12" y2="23"></line>
          <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"></path>
        </svg>
      </span>
      <span class="price-summary">
        @if (prices(); as current) {
          @for (service of availableServices(); track service.id) {
            <span class="price-item">
              <span class="service-label">{{ service.label }}</span>
              @if (current[service.key] !== null) {
                <strong class="price-val">{{ current[service.key] | number:'1.0-2':'vi' }} <small>đ/{{ service.unit }}</small></strong>
              } @else {
                <strong class="price-val unpriced">Chưa đặt giá</strong>
              }
            </span>
          }
        } @else {
          <span class="price-val loading">{{ loadFailed() ? 'Lỗi tải giá' : 'Đang tải giá…' }}</span>
        }
      </span>
      <span class="price-edit-action">
        <svg viewBox="0 0 24 24" width="11" height="11" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"></path>
          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"></path>
        </svg>
        <span>Chỉnh giá</span>
      </span>
    </button>

    <dialog #editor aria-labelledby="price-editor-title" (close)="editing.set(false)"
      (cancel)="keepOpenWhileSaving($event)">
      <header class="editor-header">
          <div>
            <h2 id="price-editor-title">Bảng giá tại trạm</h2>
            <p>{{ station().name }}</p>
          </div>
          <button type="button" class="close-button" aria-label="Đóng bảng giá"
            [disabled]="saving() !== null" (click)="editor.close()">×</button>
      </header>

      @if (editing()) {
        <div class="price-forms">
          @if (prices(); as current) {
            @for (service of availableServices(); track service.id) {
              <form #priceForm="ngForm" (ngSubmit)="save(service, priceInput.value)">
                <label [for]="'station-price-' + service.id">{{ service.label }}</label>
                <div class="price-field">
                  <input [id]="'station-price-' + service.id" name="price" #priceInput="ngModel"
                    type="number" inputmode="decimal" min="0" [max]="service.maximum" step="0.01"
                    required [ngModel]="current[service.key]" [disabled]="saving() !== null"
                    [attr.aria-describedby]="'station-price-unit-' + service.id">
                  <span [id]="'station-price-unit-' + service.id">đ/{{ service.unit }}</span>
                  <button type="submit" [disabled]="saving() !== null || priceForm.invalid ||
                    priceInput.value === current[service.key]">
                    {{ saving() === service.id ? 'Đang lưu…' : 'Lưu giá' }}
                  </button>
                </div>
                @if (priceInput.invalid && priceInput.touched) {
                  <p class="field-error">Nhập giá từ 0 đến {{ service.maximum | number:'1.0-0':'vi' }} đ.</p>
                }
              </form>
            }
            <p class="editor-note">Giá mới áp dụng ngay cho phiên sạc bắt đầu sau đó và lượt đổi pin chưa hoàn tất.</p>
          } @else if (loadFailed()) {
            <p role="alert">Chưa tải được đơn giá hiện tại.</p>
            <button type="button" class="retry-button" (click)="retry()">Tải lại giá</button>
          } @else {
            <p role="status">Đang tải bảng giá…</p>
          }
          @if (saveError(); as message) { <p class="field-error" role="alert">{{ message }}</p> }
          @if (savedLabel(); as label) { <p class="save-status" role="status">Đã lưu giá {{ label }}.</p> }
        </div>
      }
    </dialog>
  `,
  styleUrl: './station-pricing.component.scss',
})
export class StationPricingComponent {
  private readonly operatorService = inject(OperatorService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refresh = new Subject<void>();
  readonly station = input.required<OperatorLocationInfo>();
  private readonly stationId = computed(() => this.station().id);
  readonly availableServices = computed(() => services.filter((service) => service.id === 1
    ? this.station().offersCarCharging : this.station().offersBatterySwap));
  readonly prices = signal<CurrentStationTariffs | null>(null);
  readonly loadFailed = signal(false);
  readonly editing = signal(false);
  readonly saving = signal<number | null>(null);
  readonly saveError = signal<string | null>(null);
  readonly savedLabel = signal<string | null>(null);

  constructor() {
    merge(toObservable(this.stationId), this.refresh).pipe(
      switchMap(() => {
        this.prices.set(null);
        this.loadFailed.set(false);
        return this.operatorService.getCurrentTariffs().pipe(catchError(() => {
          this.loadFailed.set(true);
          return of(null);
        }));
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((prices) => this.prices.set(prices));
  }

  open(dialog: HTMLDialogElement): void {
    this.saveError.set(null);
    this.savedLabel.set(null);
    this.editing.set(true);
    dialog.showModal();
    if (this.prices() !== null || this.loadFailed()) this.retry();
  }

  retry(): void { this.refresh.next(); }

  keepOpenWhileSaving(event: Event): void {
    if (this.saving() !== null) event.preventDefault();
  }

  save(service: (typeof services)[number], price: number | null): void {
    if (price === null || !Number.isFinite(price) || price < 0 || price > service.maximum ||
        this.saving() !== null || price === this.prices()?.[service.key]) return;
    const stationId = this.stationId();
    this.saving.set(service.id);
    this.saveError.set(null);
    this.savedLabel.set(null);
    this.operatorService.createTariff(service.id, price).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.saving.set(null)),
    ).subscribe({
      next: (tariff) => {
        if (stationId !== this.stationId()) return;
        this.prices.update((current) => current && { ...current, [service.key]: tariff.unitPrice });
        this.savedLabel.set(service.label.toLocaleLowerCase('vi-VN'));
      },
      error: () => this.saveError.set('Không lưu được đơn giá. Vui lòng thử lại.'),
    });
  }
}
