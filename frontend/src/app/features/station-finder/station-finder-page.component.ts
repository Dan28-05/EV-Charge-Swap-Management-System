import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { directionsUrl, formatDistance, serviceLabel, Station } from './models/station.model';
import { AuthService } from '../auth/services/auth.service';
import { LocationsApiService } from './services/locations-api.service';
import { StationListComponent } from './station-list/station-list.component';
import { StationMapComponent } from './station-map/station-map.component';

@Component({
  selector: 'app-station-finder-page',
  imports: [FormsModule, RouterLink, StationListComponent, StationMapComponent],
  templateUrl: './station-finder-page.component.html',
  styleUrl: './station-finder-page.component.scss',
})
export class StationFinderPageComponent implements OnInit {
  private readonly api = inject(LocationsApiService);
  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private detailRequest?: Subscription;
  private page = 0;

  readonly currentUser = this.authService.currentUser;
  readonly stations = signal<Station[]>([]);
  readonly selectedId = signal<string | null>(null);
  readonly detail = signal<Station | null>(null);
  readonly total = signal(0);
  readonly hasMore = signal(true);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly detailLoading = signal(false);
  readonly detailError = signal('');
  readonly mobileView = signal<'list' | 'map'>('list');
  readonly directionsUrl = directionsUrl;
  readonly serviceLabel = serviceLabel;
  readonly formatDistance = formatDistance;

  // Trạng thái định vị GPS và bán kính tìm kiếm (Google Maps style)
  readonly userLocation = signal<{ lat: number; lng: number } | null>(null);
  readonly selectedRadiusKm = signal<number>(10); // Mặc định tìm trong 10km
  readonly radiusOptions = [5, 10, 25, 50]; // Lựa chọn bán kính km

  // Tiện ích tìm kiếm & bộ lọc kiểu Google Maps
  readonly searchQuery = signal('');
  readonly activeFilter = signal<'all' | 'charging' | 'swap'>('all');
  readonly sidebarCollapsed = signal(false);
  readonly copied = signal(false);

  readonly displayedStations = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    const filter = this.activeFilter();
    const list = this.stations();

    if (!query && filter === 'all') return list;

    return list.filter((s) => {
      const matchesQuery =
        !query || s.name.toLowerCase().includes(query) || s.address.toLowerCase().includes(query);
      const matchesFilter =
        filter === 'all' ||
        (filter === 'charging' && s.offersCarCharging) ||
        (filter === 'swap' && s.offersBatterySwap);
      return matchesQuery && matchesFilter;
    });
  });

  ngOnInit() {
    this.autoDetectLocationAndLoad();
  }

  // Tự động xin quyền GPS và tìm trạm xung quanh vị trí của bạn
  private autoDetectLocationAndLoad() {
    if (typeof navigator !== 'undefined' && navigator.geolocation) {
      navigator.geolocation.getCurrentPosition(
        (pos) => {
          const coords = { lat: pos.coords.latitude, lng: pos.coords.longitude };
          this.userLocation.set(coords);
          this.searchNearby(coords.lat, coords.lng, this.selectedRadiusKm(), true);
        },
        () => {
          // Nếu từ chối GPS hoặc lỗi, tải danh sách toàn quốc mặc định
          this.loadMore();
        },
        { enableHighAccuracy: true, timeout: 6000 },
      );
    } else {
      this.loadMore();
    }
  }

  // Tìm các trạm gần nhất theo bán kính qua Backend API
  searchNearby(lat: number, lng: number, radiusKm: number, resetList = true) {
    if (resetList) {
      this.page = 0;
      this.stations.set([]);
    }
    this.loading.set(true);
    this.error.set('');

    this.api
      .nearby(
        lat,
        lng,
        radiusKm,
        this.activeFilter(),
        this.searchQuery(),
        this.page + 1,
        20,
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          const merged = new Map(this.stations().map((s) => [s.id, s]));
          for (const station of result.items) merged.set(station.id, station);
          this.stations.set([...merged.values()]);
          this.page = result.page;
          this.total.set(result.totalCount);
          this.hasMore.set(
            result.items.length > 0 && result.page * result.pageSize < result.totalCount,
          );
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Không thể tìm kiếm trạm xung quanh vị trí của bạn.');
          this.loading.set(false);
        },
      });
  }

  onUserLocated(coords: { lat: number; lng: number }) {
    this.userLocation.set(coords);
    this.searchNearby(coords.lat, coords.lng, this.selectedRadiusKm(), true);
  }

  setRadius(radiusKm: number) {
    this.selectedRadiusKm.set(radiusKm);
    const user = this.userLocation();
    if (user && radiusKm > 0) {
      this.searchNearby(user.lat, user.lng, radiusKm, true);
    } else if (radiusKm === 0) {
      this.page = 0;
      this.stations.set([]);
      this.loadMore();
    }
  }

  setFilter(filter: 'all' | 'charging' | 'swap') {
    this.activeFilter.set(filter);
    const user = this.userLocation();
    if (user && this.selectedRadiusKm() > 0) {
      this.searchNearby(user.lat, user.lng, this.selectedRadiusKm(), true);
    }
  }

  toggleSidebar() {
    this.sidebarCollapsed.update((v) => !v);
  }

  copyAddress(address: string) {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(address);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    }
  }

  loadMore() {
    const user = this.userLocation();
    if (user && this.selectedRadiusKm() > 0) {
      this.searchNearby(user.lat, user.lng, this.selectedRadiusKm(), false);
      return;
    }

    if (this.loading() || !this.hasMore()) return;
    this.loading.set(true);
    this.error.set('');
    this.api
      .list(this.page + 1)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          const merged = new Map(this.stations().map((s) => [s.id, s]));
          for (const station of result.items) merged.set(station.id, station);
          this.stations.set([...merged.values()]);
          this.page = result.page;
          this.total.set(result.totalCount);
          this.hasMore.set(
            result.items.length > 0 && result.page * result.pageSize < result.totalCount,
          );
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Không thể tải danh sách trạm. Vui lòng thử lại.');
          this.loading.set(false);
        },
      });
  }

  selectStation(id: string) {
    this.detailRequest?.unsubscribe();
    this.selectedId.set(id);
    this.detail.set(null);
    this.detailLoading.set(true);
    this.detailError.set('');
    this.detailRequest = this.api
      .detail(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (station) => {
          this.detail.set(station);
          this.detailLoading.set(false);
        },
        error: () => {
          this.detailError.set(
            'Không thể tải chi tiết. Trạm có thể đã được ẩn hoặc kết nối bị gián đoạn.',
          );
          this.detailLoading.set(false);
        },
      });
  }

  closeDetail() {
    this.detailRequest?.unsubscribe();
    this.selectedId.set(null);
    this.detail.set(null);
    this.detailLoading.set(false);
  }

  logout() {
    this.authService.clearSession();
  }
}
