import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterModule } from '@angular/router';
import * as QRCode from 'qrcode';
import { extractApiError } from '../../../core/utils/api-error.util';
import { AuthService } from '../../auth/services/auth.service';
import { Station } from '../../station-finder/models/station.model';
import { LocationsApiService } from '../../station-finder/services/locations-api.service';
import { ReservationServiceType } from '../models/reservation.models';
import { ReservationService } from '../services/reservation.service';

@Component({
  selector: 'app-customer-reservation-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, RouterModule],
  templateUrl: './customer-reservation-modal.component.html',
  styleUrl: './customer-reservation-modal.component.scss',
})
export class CustomerReservationModalComponent implements OnInit, OnChanges {
  private readonly reservationService = inject(ReservationService);
  private readonly authService = inject(AuthService);
  private readonly locationsApi = inject(LocationsApiService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  @Input() station: Station | null = null;
  @Input() userLocation: { lat: number; lng: number } | null = null;
  @Input() initialServiceType: ReservationServiceType | null = null;
  @Output() closed = new EventEmitter<void>();

  readonly distanceWarnKm = 10;
  readonly distanceBlockKm = 20;

  readonly distanceToStationKm = computed<number | null>(() => {
    const s = this.stationData();
    const u = this.userLocationSignal();
    if (!s) return null;
    if (typeof s.distanceKm === 'number' && Number.isFinite(s.distanceKm) && s.distanceKm >= 0 && u) {
      return Math.round(s.distanceKm * 10) / 10;
    }
    if (!u || !Number.isFinite(s.latitude) || !Number.isFinite(s.longitude)) return null;
    const km = this.haversineKm(u.lat, u.lng, s.latitude, s.longitude);
    return Math.round(km * 10) / 10;
  });

  readonly isTooFarToBook = computed(() => {
    const km = this.distanceToStationKm();
    return km !== null && km > this.distanceBlockKm;
  });

  readonly isFarWarning = computed(() => {
    const km = this.distanceToStationKm();
    return km !== null && km > this.distanceWarnKm && km <= this.distanceBlockKm;
  });

  private readonly userLocationSignal = signal<{ lat: number; lng: number } | null>(null);

  readonly currentUser = this.authService.currentUser;
  readonly activeReservation = this.reservationService.activeReservation;
  readonly lastClosedReservation = this.reservationService.lastClosedReservation;
  readonly stationData = signal<Station | null>(null);
  readonly remainingSeconds = this.reservationService.remainingSeconds;
  readonly formattedCountdown = this.reservationService.formattedCountdown;
  readonly loading = this.reservationService.loading;

  readonly selectedServiceType = signal<ReservationServiceType>(ReservationServiceType.Charging);
  readonly qrDataUrl = signal<string>('');
  readonly errorMessage = signal<string | null>(null);
  readonly showCancelConfirm = signal<boolean>(false);
  readonly copiedCode = signal<boolean>(false);

  private qrRenderSeq = 0;
  private copiedTimer: ReturnType<typeof setTimeout> | null = null;

  readonly hasActive = computed(() => !!this.activeReservation());
  readonly isHeld = computed(() => this.activeReservation()?.status === 'Held');
  readonly isCheckedIn = computed(() => this.activeReservation()?.status === 'CheckedIn');

  readonly formattedCode = computed(() => {
    const code = this.activeReservation()?.code;
    if (!code) return '';
    return code.match(/.{1,4}/g)?.join(' ') ?? code;
  });

  readonly progressPercentage = computed(() => {
    const total = 15 * 60; // 15 mins
    const remaining = this.remainingSeconds();
    return Math.max(0, Math.min(100, Math.round((remaining / total) * 100)));
  });

  constructor() {
    this.destroyRef.onDestroy(() => {
      if (this.copiedTimer) clearTimeout(this.copiedTimer);
    });

    // Reactively regenerate QR code and hydrate station details whenever activeReservation arrives or updates.
    effect(() => {
      const res = this.activeReservation();
      void this.generateQrFor(res?.status === 'Held' ? res.code : null);

      if (!this.station && res?.locationId && this.stationData()?.id !== res.locationId) {
        this.locationsApi.detail(res.locationId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
          next: (s) => this.stationData.set(s),
          error: () => {},
        });
      }
    });
  }

  ngOnInit(): void {
    this.userLocationSignal.set(this.userLocation);
    if (this.currentUser()) {
      this.reservationService.getActive().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        error: () => {},
      });
    }
    this.syncServiceType();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['userLocation']) {
      this.userLocationSignal.set(this.userLocation);
    }
    if (changes['station'] || changes['initialServiceType']) {
      this.syncServiceType();
    }
  }

  private syncServiceType(): void {
    if (this.station) {
      this.stationData.set(this.station);
    }

    if (this.initialServiceType) {
      this.selectedServiceType.set(this.initialServiceType);
    } else if (this.station) {
      const hasCharging = this.station.offersCarCharging && (this.station.availableChargingBays ?? 0) > 0;
      const hasSwap = this.station.offersBatterySwap && (this.station.availableBatteries ?? 0) > 0;
      if (hasCharging) {
        this.selectedServiceType.set(ReservationServiceType.Charging);
      } else if (hasSwap) {
        this.selectedServiceType.set(ReservationServiceType.BatterySwap);
      } else if (this.station.offersCarCharging) {
        this.selectedServiceType.set(ReservationServiceType.Charging);
      } else if (this.station.offersBatterySwap) {
        this.selectedServiceType.set(ReservationServiceType.BatterySwap);
      }
    }
  }

  private async generateQrFor(code: string | null): Promise<void> {
    const seq = ++this.qrRenderSeq;
    if (!code) {
      this.qrDataUrl.set('');
      return;
    }

    try {
      const url = await QRCode.toDataURL(code, {
        width: 105,
        margin: 1,
        color: {
          dark: '#0f172a',
          light: '#ffffff',
        },
      });
      if (seq === this.qrRenderSeq) {
        this.qrDataUrl.set(url);
      }
    } catch {
      if (seq === this.qrRenderSeq) {
        this.qrDataUrl.set('');
      }
    }
  }

  selectType(type: ReservationServiceType): void {
    this.selectedServiceType.set(type);
    this.errorMessage.set(null);
  }

  confirmHold(): void {
    if (!this.currentUser()) {
      this.router.navigate(['/login']);
      return;
    }

    if (!this.station) return;
    if (this.isTooFarToBook()) {
      this.errorMessage.set(`Trạm cách bạn ${this.distanceToStationKm()} km (vượt quá giới hạn ${this.distanceBlockKm} km cho giữ chỗ 15 phút). Vui lòng chọn trạm gần hơn.`);
      return;
    }

    this.errorMessage.set(null);
    this.reservationService
      .hold({
        locationId: this.station.id,
        serviceType: this.selectedServiceType(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {},
        error: (err: unknown) => {
          const detail = extractApiError(err, 'Không thể giữ chỗ vào lúc này. Vui lòng thử lại sau.');
          this.errorMessage.set(this.translateBackendError(detail));
        },
      });
  }

  copyCode(): void {
    const code = this.activeReservation()?.code;
    if (!code || typeof navigator === 'undefined' || !navigator.clipboard) return;

    navigator.clipboard.writeText(code).then(() => {
      this.copiedCode.set(true);
      if (this.copiedTimer) clearTimeout(this.copiedTimer);
      this.copiedTimer = setTimeout(() => this.copiedCode.set(false), 2500);
    }).catch(() => {});
  }

  requestCancel(): void {
    this.showCancelConfirm.set(true);
  }

  dismissCancelConfirm(): void {
    this.showCancelConfirm.set(false);
  }

  executeCancel(): void {
    const current = this.activeReservation();
    if (!current) return;

    this.reservationService.cancel(current.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.showCancelConfirm.set(false);
        this.closed.emit();
      },
      error: (err: unknown) => {
        const detail = extractApiError(err, 'Không thể hủy lượt đặt chỗ vào lúc này.');
        this.errorMessage.set(this.translateBackendError(detail));
        this.showCancelConfirm.set(false);
      },
    });
  }

  refreshActiveStatus(): void {
    this.reservationService.getActive().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (res) => {
        if (!res) {
          this.errorMessage.set(null);
        }
      },
      error: () => {},
    });
  }

  close(): void {
    this.closed.emit();
  }

  googleMapsUrl(): string {
    const st = this.stationData() || this.station;
    if (!st) return 'https://maps.google.com';
    return `https://www.google.com/maps/dir/?api=1&destination=${st.latitude},${st.longitude}`;
  }

  private haversineKm(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const toRad = (deg: number) => (deg * Math.PI) / 180;
    const dLat = toRad(lat2 - lat1);
    const dLon = toRad(lon2 - lon1);
    const a =
      Math.sin(dLat / 2) ** 2 +
      Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2;
    return 6371 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  }

  private translateBackendError(detail: string): string {
    if (detail.includes('already have an active reservation')) {
      return 'Bạn đang có một lượt giữ chỗ đang hoạt động. Vui lòng hoàn tất hoặc hủy lượt hiện tại trước khi đặt chỗ mới.';
    }
    if (detail.includes('No resource is available')) {
      return 'Hiện tại không còn cổng sạc hoặc pin sẵn sàng tại trạm này. Vui lòng chọn trạm khác.';
    }
    if (detail.includes('not open for booking')) {
      return 'Dịch vụ này hiện chưa mở hoặc ngoài giờ phục vụ của trạm.';
    }
    if (detail.includes('unexpired hold can be cancelled')) {
      return 'Chỉ có thể hủy lượt giữ chỗ khi chưa hết hạn và chưa check-in.';
    }
    return detail;
  }
}
