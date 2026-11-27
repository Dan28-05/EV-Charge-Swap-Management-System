import { CommonModule, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import { finalize, switchMap } from 'rxjs';
import { extractApiError } from '../../../core/utils/api-error.util';
import { PlaceSearchResult } from '../../station-finder/models/place-search.model';
import { PlaceSearchService } from '../../station-finder/services/place-search.service';
import { STATION_FINDER_CONFIG } from '../../station-finder/station-finder.config';
import {
  AdminLocationResources,
  AdminOperatorSummary,
  LocationUpsertPayload,
  StationOperatingStatus,
} from '../models/admin.models';
import { AdminService } from '../services/admin.service';

export type WizardStep = 1 | 2 | 3 | 4;

@Component({
  selector: 'app-station-onboarding-wizard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, DecimalPipe, FormsModule],
  templateUrl: './station-onboarding-wizard.component.html',
  styleUrl: './station-onboarding-wizard.component.scss',
})
export class StationOnboardingWizardComponent {
  private readonly adminService = inject(AdminService);
  private readonly placeSearch = inject(PlaceSearchService);
  private readonly mapConfig = inject(STATION_FINDER_CONFIG);
  private readonly destroyRef = inject(DestroyRef);

  readonly operators = input<AdminOperatorSummary[]>([]);
  readonly completed = output<void>();

  private readonly dialogRef = viewChild.required<ElementRef<HTMLDialogElement>>('wizardDialog');
  private readonly mapHostRef = viewChild<ElementRef<HTMLDivElement>>('pickerMap');

  private mapInstance: L.Map | null = null;
  private markerInstance: L.Marker | null = null;

  readonly step = signal<WizardStep>(1);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);

  // Active station being onboarded (null until Step 1 creates the draft)
  readonly stationId = signal<string | null>(null);
  readonly resources = signal<AdminLocationResources | null>(null);

  // Step 1 fields (Info & Map Picker)
  readonly name = signal('');
  readonly address = signal('');
  readonly latitude = signal<number>(13.7669);
  readonly longitude = signal<number>(109.2122);
  readonly offersCarCharging = signal(true);
  readonly offersBatterySwap = signal(true);
  readonly chargingPowerKw = signal<number>(150);

  // Address geocode search
  readonly placeQuery = signal('');
  readonly placeSearching = signal(false);
  readonly placeResults = signal<PlaceSearchResult[]>([]);

  // Step 2 fields (Batch Provisioning)
  readonly bayPrefix = signal('BAY-');
  readonly bayCount = signal(4);
  readonly bayStartNumber = signal(1);
  readonly bayPowerKw = signal(150);

  readonly batPrefix = signal('BAT-');
  readonly batCount = signal(8);
  readonly batStartNumber = signal(1);

  // Step 3 fields (Hours Preset & Operator)
  readonly selectedPreset = signal<'24/7' | 'business' | 'extended'>('24/7');
  readonly selectedOperatorId = signal<string>('');

  readonly bayPreview = computed(() => {
    const prefix = this.bayPrefix().trim().toUpperCase() || 'BAY-';
    const start = Math.max(1, Math.floor(this.bayStartNumber() || 1));
    const count = Math.min(20, Math.max(1, Math.floor(this.bayCount() || 1)));
    const first = `${prefix}${String(start).padStart(2, '0')}`;
    const last = `${prefix}${String(start + count - 1).padStart(2, '0')}`;
    return count === 1 ? first : `${first} → ${last} (${count} trụ)`;
  });

  readonly batteryPreview = computed(() => {
    const prefix = this.batPrefix().trim().toUpperCase() || 'BAT-';
    const start = Math.max(1, Math.floor(this.batStartNumber() || 1));
    const count = Math.min(50, Math.max(1, Math.floor(this.batCount() || 1)));
    const first = `${prefix}${String(start).padStart(2, '0')}`;
    const last = `${prefix}${String(start + count - 1).padStart(2, '0')}`;
    return count === 1 ? first : `${first} → ${last} (${count} pin)`;
  });

  readonly canSubmitStep1 = computed(() => {
    return (
      this.name().trim().length >= 2 &&
      this.address().trim().length >= 3 &&
      (this.offersCarCharging() || this.offersBatterySwap()) &&
      Number.isFinite(this.latitude()) &&
      Number.isFinite(this.longitude())
    );
  });

  constructor() {
    this.destroyRef.onDestroy(() => this.destroyMap());
  }

  openNew(): void {
    this.resetState();
    this.dialogRef().nativeElement.showModal();
    setTimeout(() => this.initMap(), 50);
  }

  openExistingDraft(stationId: string, startStep: WizardStep = 2): void {
    this.resetState();
    this.stationId.set(stationId);
    this.step.set(startStep);
    this.dialogRef().nativeElement.showModal();
    this.loadResources(stationId, true);
  }

  close(): void {
    this.destroyMap();
    const dlg = this.dialogRef().nativeElement;
    if (dlg.open) dlg.close();
    if (this.stationId()) {
      this.completed.emit();
    }
  }

  goToStep(target: WizardStep): void {
    if (target > 1 && !this.stationId()) return;
    this.error.set(null);
    this.notice.set(null);
    this.step.set(target);
    if (target === 1) {
      setTimeout(() => this.initMap(), 50);
    } else {
      this.destroyMap();
      if (this.stationId()) {
        this.loadResources(this.stationId()!, false);
      }
    }
  }

  searchPlaces(): void {
    const q = this.placeQuery().trim();
    if (q.length < 2) return;
    this.placeSearching.set(true);
    this.placeSearch
      .search(q)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.placeSearching.set(false)),
      )
      .subscribe({
        next: (results) => this.placeResults.set(results),
        error: () => this.placeResults.set([]),
      });
  }

  pickPlace(place: PlaceSearchResult): void {
    this.latitude.set(Math.round(place.latitude * 1_000_000) / 1_000_000);
    this.longitude.set(Math.round(place.longitude * 1_000_000) / 1_000_000);
    if (!this.address().trim()) {
      this.address.set(place.label);
    }
    this.placeResults.set([]);
    this.placeQuery.set(place.label);
    this.updateMarkerPosition(place.latitude, place.longitude, true);
  }

  saveStep1Draft(): void {
    if (!this.canSubmitStep1() || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    const payload: LocationUpsertPayload = {
      name: this.name().trim(),
      address: this.address().trim(),
      latitude: Number(this.latitude()),
      longitude: Number(this.longitude()),
      offersCarCharging: this.offersCarCharging(),
      offersBatterySwap: this.offersBatterySwap(),
      operatingStatus: (this.resources()?.station.location.operatingStatus as StationOperatingStatus | undefined) ?? 'Closed',
      isActive: this.stationId() ? undefined : false,
      isOpen24Hours: this.resources()?.station.location.isOpen24Hours ?? false,
      chargingPowerKw: this.offersCarCharging() ? this.chargingPowerKw() : null,
      batteryCapacityKwh: this.offersBatterySwap() ? 1.5 : null,
    };

    const existingId = this.stationId();
    const request$ = existingId
      ? this.adminService.updateLocation(existingId, payload)
      : this.adminService.createLocation(payload);

    request$
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (station) => {
          this.stationId.set(station.id);
          this.bayPowerKw.set(station.chargingPowerKw ?? 150);
          const slug = station.name
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '')
            .replace(/[^A-Za-z0-9]+/g, '')
            .toUpperCase()
            .slice(0, 6);
          if (slug) {
            this.batPrefix.set(`BAT-${slug}-`);
          }
          this.destroyMap();
          this.step.set(2);
          this.loadResources(station.id, false);
        },
        error: (err: unknown) => {
          this.error.set(extractApiError(err, 'Không thể lưu thông tin trạm. Vui lòng kiểm tra lại dữ liệu.'));
        },
      });
  }

  provisionBays(): void {
    const id = this.stationId();
    if (!id || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    this.adminService
      .batchCreateBays(id, {
        prefix: this.bayPrefix().trim().toUpperCase(),
        count: Math.min(20, Math.max(1, Number(this.bayCount()) || 1)),
        startNumber: Math.max(1, Number(this.bayStartNumber()) || 1),
        powerKw: Number(this.bayPowerKw()) || 150,
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (res) => {
          this.notice.set(
            res.skippedCount > 0
              ? `Đã khởi tạo ${res.createdCount} trụ sạc mới (bỏ qua ${res.skippedCount} mã trùng).`
              : `Đã khởi tạo thành công ${res.createdCount} trụ sạc mới.`,
          );
          this.loadResources(id, false);
        },
        error: (err: unknown) => {
          this.error.set(extractApiError(err, 'Không thể khởi tạo hàng loạt trụ sạc.'));
        },
      });
  }

  provisionBatteries(): void {
    const id = this.stationId();
    if (!id || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    this.adminService
      .batchCreateBatteries(id, {
        prefix: this.batPrefix().trim().toUpperCase(),
        count: Math.min(50, Math.max(1, Number(this.batCount()) || 1)),
        startNumber: Math.max(1, Number(this.batStartNumber()) || 1),
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (res) => {
          this.notice.set(
            res.skippedCount > 0
              ? `Đã nhập kho ${res.createdCount} viên pin sẵn sàng (bỏ qua ${res.skippedCount} mã trùng).`
              : `Đã nhập kho thành công ${res.createdCount} viên pin sẵn sàng.`,
          );
          this.loadResources(id, false);
        },
        error: (err: unknown) => {
          this.error.set(extractApiError(err, 'Không thể nhập kho hàng loạt pin.'));
        },
      });
  }

  applyHoursPreset(): void {
    const id = this.stationId();
    if (!id || this.busy()) return;
    const preset = this.selectedPreset();
    const payload =
      preset === '24/7'
        ? { opensAtMinute: 0, closesAtMinute: 1440 }
        : preset === 'extended'
          ? { opensAtMinute: 360, closesAtMinute: 1320 }
          : { opensAtMinute: 420, closesAtMinute: 1260 };

    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    this.adminService
      .applyServiceHoursPreset(id, payload)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.notice.set('Đã áp dụng lịch hoạt động 7 ngày cho toàn bộ dịch vụ của trạm.');
          this.loadResources(id, false);
        },
        error: (err: unknown) => {
          this.error.set(extractApiError(err, 'Không thể thiết lập giờ hoạt động.'));
        },
      });
  }

  assignSelectedOperator(): void {
    const id = this.stationId();
    const operatorId = this.selectedOperatorId();
    if (!id || !operatorId || this.busy()) return;

    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    this.adminService
      .assignOperator(operatorId, id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.notice.set('Đã phân công nhân viên trực trạm thành công.');
          this.selectedOperatorId.set('');
          this.loadResources(id, false);
        },
        error: (err: unknown) => {
          this.error.set(extractApiError(err, 'Không thể phân công nhân viên cho trạm này.'));
        },
      });
  }

  publishGoLive(): void {
    const id = this.stationId();
    const res = this.resources();
    if (!id || !res || !res.readiness.canGoLive || this.busy()) return;

    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    const loc = res.station.location;
    const payload: LocationUpsertPayload = {
      name: loc.name,
      address: loc.address,
      latitude: loc.latitude,
      longitude: loc.longitude,
      offersCarCharging: loc.offersCarCharging,
      offersBatterySwap: loc.offersBatterySwap,
      operatingStatus: 'Open',
      isOpen24Hours: loc.isOpen24Hours ?? false,
      chargingPowerKw: loc.chargingPowerKw ?? null,
      batteryCapacityKwh: loc.batteryCapacityKwh ?? null,
    };

    this.adminService
      .updateLocation(id, payload)
      .pipe(
        switchMap(() => this.adminService.setVisibility(id, true)),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.completed.emit();
          this.destroyMap();
          this.dialogRef().nativeElement.close();
        },
        error: (err: unknown) => {
          this.error.set(extractApiError(err, 'Không thể công bố trạm vào lúc này.'));
        },
      });
  }

  private loadResources(stationId: string, hydrateForm: boolean): void {
    this.adminService
      .getLocationResources(stationId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.resources.set(data);
          if (hydrateForm) {
            const loc = data.station.location;
            this.name.set(loc.name);
            this.address.set(loc.address);
            this.latitude.set(loc.latitude);
            this.longitude.set(loc.longitude);
            this.offersCarCharging.set(loc.offersCarCharging);
            this.offersBatterySwap.set(loc.offersBatterySwap);
            this.chargingPowerKw.set(loc.chargingPowerKw ?? 150);
            this.bayPowerKw.set(loc.chargingPowerKw ?? 150);
          }
        },
        error: (err: unknown) => {
          this.error.set(extractApiError(err, 'Không tải được dữ liệu khởi tạo trạm.'));
        },
      });
  }

  private initMap(): void {
    const host = this.mapHostRef()?.nativeElement;
    if (!host) return;
    if (this.mapInstance) {
      this.mapInstance.invalidateSize();
      return;
    }

    const lat = this.latitude();
    const lng = this.longitude();
    const map = L.map(host, {
      center: [lat, lng],
      zoom: 14,
      zoomControl: true,
    });

    L.tileLayer(this.mapConfig.tileUrl, {
      subdomains: this.mapConfig.subdomains || ['0', '1', '2', '3'],
      maxZoom: 20,
    }).addTo(map);

    const icon = L.divIcon({
      className: 'admin-picker-pin',
      html: '<div class="picker-pin-dot"></div>',
      iconSize: [24, 24],
      iconAnchor: [12, 12],
    });

    const marker = L.marker([lat, lng], { draggable: true, icon }).addTo(map);
    marker.on('dragend', () => {
      const pos = marker.getLatLng();
      this.latitude.set(Math.round(pos.lat * 1_000_000) / 1_000_000);
      this.longitude.set(Math.round(pos.lng * 1_000_000) / 1_000_000);
    });

    map.on('click', (e: L.LeafletMouseEvent) => {
      const { lat: cLat, lng: cLng } = e.latlng;
      this.latitude.set(Math.round(cLat * 1_000_000) / 1_000_000);
      this.longitude.set(Math.round(cLng * 1_000_000) / 1_000_000);
      marker.setLatLng([cLat, cLng]);
    });

    this.mapInstance = map;
    this.markerInstance = marker;
    setTimeout(() => map.invalidateSize(), 120);
  }

  private updateMarkerPosition(lat: number, lng: number, pan: boolean): void {
    if (this.markerInstance) {
      this.markerInstance.setLatLng([lat, lng]);
    }
    if (this.mapInstance && pan) {
      this.mapInstance.setView([lat, lng], 16, { animate: true });
    }
  }

  private destroyMap(): void {
    if (this.mapInstance) {
      this.mapInstance.remove();
      this.mapInstance = null;
      this.markerInstance = null;
    }
  }

  private resetState(): void {
    this.destroyMap();
    this.step.set(1);
    this.busy.set(false);
    this.error.set(null);
    this.notice.set(null);
    this.stationId.set(null);
    this.resources.set(null);
    this.name.set('');
    this.address.set('');
    this.latitude.set(13.7669);
    this.longitude.set(109.2122);
    this.offersCarCharging.set(true);
    this.offersBatterySwap.set(true);
    this.chargingPowerKw.set(150);
    this.placeQuery.set('');
    this.placeResults.set([]);
    this.bayPrefix.set('BAY-');
    this.bayCount.set(4);
    this.bayStartNumber.set(1);
    this.bayPowerKw.set(150);
    this.batPrefix.set('BAT-');
    this.batCount.set(8);
    this.batStartNumber.set(1);
    this.selectedPreset.set('24/7');
    this.selectedOperatorId.set('');
  }
}
