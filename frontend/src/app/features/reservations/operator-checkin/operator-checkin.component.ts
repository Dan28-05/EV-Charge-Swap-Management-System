import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AuthService } from '../../auth/services/auth.service';
import { LocationsApiService } from '../../station-finder/services/locations-api.service';
import { Station } from '../../station-finder/models/station.model';
import { ReservationResponse } from '../models/reservation.models';
import { ReservationService } from '../services/reservation.service';

@Component({
  selector: 'app-operator-checkin',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './operator-checkin.component.html',
  styleUrl: './operator-checkin.component.scss',
})
export class OperatorCheckinComponent implements OnInit {
  private readonly reservationService = inject(ReservationService);
  private readonly authService = inject(AuthService);
  private readonly locationsApi = inject(LocationsApiService);

  readonly currentUser = this.authService.currentUser;
  readonly loading = signal<boolean>(false);
  readonly stationsLoading = signal<boolean>(false);
  readonly stations = signal<Station[]>([]);
  readonly selectedStation = signal<Station | null>(null);
  readonly stationSearchTerm = signal<string>('');

  readonly filteredStations = computed<Station[]>(() => {
    const term = this.stationSearchTerm().trim().toLowerCase();
    const list = this.stations();
    if (!term) return list;
    return list.filter((s) => s.name.toLowerCase().includes(term) || s.address.toLowerCase().includes(term));
  });

  readonly reservationCode = signal<string>('');
  readonly currentReservation = signal<ReservationResponse | null>(null);
  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);
  readonly showCloseConfirm = signal<boolean>(false);

  ngOnInit(): void {
    this.loadStations();
  }

  loadStations(): void {
    this.stationsLoading.set(true);
    this.locationsApi
      .list(1, 100)
      .subscribe({
        next: (res) => {
          this.stationsLoading.set(false);
          const openStations = res.items.filter((s: Station) => s.operatingStatus === 'Open');
          this.stations.set(openStations.length > 0 ? openStations : res.items);
          if (this.stations().length > 0) {
            this.selectedStation.set(this.stations()[0]);
          }
        },
        error: () => {
          this.stationsLoading.set(false);
        },
      });
  }

  onStationSelect(stationId: string): void {
    const found = this.stations().find((s) => s.id === stationId) || null;
    this.selectedStation.set(found);
    this.errorMessage.set(null);
    this.successMessage.set(null);
  }

  onSearchChange(term: string): void {
    this.stationSearchTerm.set(term);
    const filtered = this.filteredStations();
    if (filtered.length > 0) {
      const current = this.selectedStation();
      if (!current || !filtered.some((s) => s.id === current.id)) {
        this.selectedStation.set(filtered[0]);
      }
    }
  }

  onCodeInput(val: string): void {
    const cleaned = val.replace(/[^a-fA-F0-9]/g, '').toUpperCase().slice(0, 32);
    this.reservationCode.set(cleaned);
    this.errorMessage.set(null);
  }

  async pasteFromClipboard(): Promise<void> {
    try {
      const text = await navigator.clipboard.readText();
      if (text) {
        this.onCodeInput(text);
      }
    } catch {
      // ignore
    }
  }

  submitCheckIn(): void {
    const code = this.reservationCode().trim();
    const station = this.selectedStation();

    if (!station) {
      this.errorMessage.set('Vui lòng chọn trạm bạn đang làm việc.');
      return;
    }

    if (code.length !== 32) {
      this.errorMessage.set('Mã giữ chỗ phải đúng 32 ký tự hex.');
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.reservationService
      .checkIn({
        locationId: station.id,
        code,
      })
      .subscribe({
        next: (res) => {
          this.loading.set(false);
          this.currentReservation.set(res);
          this.successMessage.set('Check-in thành công! Khách hàng đã được xác nhận đến trạm.');
        },
        error: (err) => {
          this.loading.set(false);
          const detail =
            err.error?.detail || err.error?.title || 'Mã không hợp lệ, đã hết hạn hoặc đã được sử dụng.';
          this.errorMessage.set(this.translateOperatorError(detail));
        },
      });
  }

  openCloseConfirm(): void {
    this.showCloseConfirm.set(true);
  }

  dismissCloseConfirm(): void {
    this.showCloseConfirm.set(false);
  }

  executeCloseWithoutService(): void {
    const res = this.currentReservation();
    const station = this.selectedStation();
    if (!res || !station) return;

    this.loading.set(true);
    this.reservationService.closeWithoutService(res.id, station.id).subscribe({
      next: () => {
        this.loading.set(false);
        this.showCloseConfirm.set(false);
        this.successMessage.set('Đã đóng lượt giữ chỗ không dùng dịch vụ và giải phóng tài nguyên trạm.');
        this.currentReservation.update((curr) =>
          curr
            ? {
                ...curr,
                status: 'Completed',
                closingReason: curr.chargingBayId ? 'DID_NOT_CHARGE' : 'DID_NOT_SWAP',
              }
            : null,
        );
      },
      error: (err) => {
        this.loading.set(false);
        this.showCloseConfirm.set(false);
        const detail =
          err.error?.detail || 'Không thể đóng lượt giữ chỗ này. Cổng sạc có thể vẫn đang có xe.';
        this.errorMessage.set(this.translateOperatorError(detail));
      },
    });
  }

  resetDesk(): void {
    this.reservationCode.set('');
    this.currentReservation.set(null);
    this.errorMessage.set(null);
    this.successMessage.set(null);
  }

  private translateOperatorError(detail: string): string {
    if (detail.includes('invalid, expired, or already used')) {
      return 'Mã giữ chỗ không hợp lệ, đã hết hạn hoặc đã được check-in trước đó.';
    }
    if (detail.includes('not checked in or the bay is still occupied')) {
      return 'Lượt giữ chỗ chưa check-in hoặc cổng sạc vẫn đang có phương tiện đỗ.';
    }
    if (detail.includes('Forbidden') || detail.includes('403')) {
      return 'Bạn không được phân công vận hành tại trạm này.';
    }
    return detail;
  }
}
