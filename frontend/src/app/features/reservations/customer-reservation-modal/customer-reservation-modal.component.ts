import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, computed, inject, signal, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import * as QRCode from 'qrcode';
import { AuthService } from '../../auth/services/auth.service';
import { Station } from '../../station-finder/models/station.model';
import { LocationsApiService } from '../../station-finder/services/locations-api.service';
import { ReservationServiceType, ReservationResponse } from '../models/reservation.models';
import { ReservationService } from '../services/reservation.service';

@Component({
  selector: 'app-customer-reservation-modal',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './customer-reservation-modal.component.html',
  styleUrl: './customer-reservation-modal.component.scss',
})
export class CustomerReservationModalComponent implements OnInit, OnChanges {
  private readonly reservationService = inject(ReservationService);
  private readonly authService = inject(AuthService);
  private readonly locationsApi = inject(LocationsApiService);
  private readonly router = inject(Router);

  @Input() station: Station | null = null;
  @Input() initialServiceType: ReservationServiceType | null = null;
  @Output() closed = new EventEmitter<void>();

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

  ngOnInit(): void {
    if (this.currentUser()) {
      this.reservationService.getActive().subscribe();
    }
    this.syncServiceType();
    this.refreshQrCode();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['station'] || changes['initialServiceType']) {
      this.syncServiceType();
    }
  }

  private syncServiceType(): void {
    if (this.station) {
      this.stationData.set(this.station);
    } else {
      const active = this.activeReservation();
      if (active?.locationId) {
        this.locationsApi.detail(active.locationId).subscribe({
          next: (s) => this.stationData.set(s),
          error: () => {},
        });
      }
    }

    if (this.initialServiceType) {
      this.selectedServiceType.set(this.initialServiceType);
    } else if (this.station) {
      if (this.station.offersCarCharging) {
        this.selectedServiceType.set(ReservationServiceType.Charging);
      } else if (this.station.offersBatterySwap) {
        this.selectedServiceType.set(ReservationServiceType.BatterySwap);
      }
    }
    this.refreshQrCode();
  }

  async refreshQrCode(): Promise<void> {
    const res = this.activeReservation();
    if (res && res.code && res.status === 'Held') {
      try {
        const url = await QRCode.toDataURL(res.code, {
          width: 105,
          margin: 1,
          color: {
            dark: '#0f172a',
            light: '#ffffff',
          },
        });
        this.qrDataUrl.set(url);
      } catch (err) {
        console.error('Failed to generate QR code', err);
      }
    } else {
      this.qrDataUrl.set('');
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

    this.errorMessage.set(null);
    this.reservationService
      .hold({
        locationId: this.station.id,
        serviceType: this.selectedServiceType(),
      })
      .subscribe({
        next: () => {
          this.refreshQrCode();
        },
        error: (err) => {
          const detail = err.error?.detail || err.error?.title || 'Không thể giữ chỗ vào lúc này. Vui lòng thử lại sau.';
          this.errorMessage.set(this.translateBackendError(detail));
        },
      });
  }

  copyCode(): void {
    const code = this.activeReservation()?.code;
    if (!code) return;

    navigator.clipboard.writeText(code).then(() => {
      this.copiedCode.set(true);
      setTimeout(() => this.copiedCode.set(false), 2500);
    });
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

    this.reservationService.cancel(current.id).subscribe({
      next: () => {
        this.showCancelConfirm.set(false);
        this.closed.emit();
      },
      error: (err) => {
        const detail = err.error?.detail || 'Không thể hủy lượt đặt chỗ vào lúc này.';
        this.errorMessage.set(this.translateBackendError(detail));
        this.showCancelConfirm.set(false);
      },
    });
  }

  refreshActiveStatus(): void {
    this.reservationService.getActive().subscribe({
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
