import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import {
  directionsUrl,
  formatDistance,
  operatingStatusLabel,
  serviceLabel,
  Station,
} from './models/station.model';
import { AuthService } from '../auth/services/auth.service';
import { LocationsApiService } from './services/locations-api.service';
import { PlaceSearchService } from './services/place-search.service';
import { PlaceSearchResult } from './models/place-search.model';
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
  private readonly placeSearch = inject(PlaceSearchService);
  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private detailRequest?: Subscription;
  private stationRequest?: Subscription;
  private globalQuery = '';
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
  readonly operatingStatusLabel = operatingStatusLabel;
  readonly formatDistance = formatDistance;

  // Trạng thái định vị GPS và bán kính tìm kiếm (Google Maps style)
  readonly userLocation = signal<{ lat: number; lng: number } | null>(null);
  readonly searchCenter = signal<{ lat: number; lng: number } | null>(null);
  readonly selectedRadiusKm = signal<number>(50); // Tải trạm trong 50 km quanh khu vực đang xem
  readonly radiusOptions = [5, 10, 25, 50]; // Lựa chọn bán kính km

  // Tiện ích tìm kiếm & bộ lọc kiểu Google Maps
  readonly searchQuery = signal('');
  readonly activeServiceFilter = signal<'all' | 'charging' | 'swap'>('all');
  readonly availabilityFilter = signal<'all' | 'open' | 'available'>('all');
  readonly sidebarCollapsed = signal(false);
  readonly copied = signal(false);
  readonly placeResults = signal<PlaceSearchResult[]>([]);
  readonly placeSearchLoading = signal(false);
  readonly placeSearchError = signal('');

  readonly displayedStations = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    const serviceFilter = this.activeServiceFilter();
    const availabilityFilter = this.availabilityFilter();
    const list = this.stations();

    if (!query && serviceFilter === 'all' && availabilityFilter === 'all') return list;

    return list.filter((s) => {
      const matchesQuery =
        !query || s.name.toLowerCase().includes(query) || s.address.toLowerCase().includes(query);
      const matchesFilter =
        serviceFilter === 'all' ||
        (serviceFilter === 'charging' && s.offersCarCharging) ||
        (serviceFilter === 'swap' && s.offersBatterySwap);
      const matchesAvailability =
        availabilityFilter === 'all' ||
        (availabilityFilter === 'open' && s.operatingStatus === 'Open') ||
        (availabilityFilter === 'available' && this.isOpenAndAvailable(s, serviceFilter));
      return matchesQuery && matchesFilter && matchesAvailability;
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
          this.searchCenter.set(coords);
          this.searchNearby(coords.lat, coords.lng, this.selectedRadiusKm(), true);
        },
        () => {
          this.stations.set([]);
          this.total.set(0);
          this.hasMore.set(false);
        },
        { enableHighAccuracy: true, timeout: 6000 },
      );
    } else {
      this.loadMore();
    }
  }

  // Tìm các trạm gần nhất theo bán kính qua Backend API
  searchNearby(lat: number, lng: number, radiusKm: number, resetList = true) {
    this.globalQuery = '';
    this.stationRequest?.unsubscribe();
    if (resetList) {
      this.page = 0;
      this.stations.set([]);
    }
    this.loading.set(true);
    this.error.set('');

    this.stationRequest = this.api
      .nearby(
        lat,
        lng,
        radiusKm,
        this.activeServiceFilter(),
        this.availabilityFilter(),
        '',
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
    this.searchCenter.set(coords);
    this.searchNearby(coords.lat, coords.lng, this.selectedRadiusKm(), true);
  }

  onMapAreaChanged(coords: { lat: number; lng: number }) {
    this.searchCenter.set(coords);
    this.searchNearby(coords.lat, coords.lng, this.selectedRadiusKm(), true);
  }

  setRadius(radiusKm: number) {
    this.selectedRadiusKm.set(radiusKm);
    const center = this.searchCenter();
    if (center) this.searchNearby(center.lat, center.lng, radiusKm, true);
  }

  setServiceFilter(filter: 'all' | 'charging' | 'swap') {
    this.activeServiceFilter.set(filter);
    this.reloadForFilters();
  }

  setAvailabilityFilter(filter: 'all' | 'open' | 'available') {
    this.availabilityFilter.set(filter);
    this.reloadForFilters();
  }

  private reloadForFilters() {
    if (this.globalQuery) return;
    const center = this.searchCenter();
    if (center) this.searchNearby(center.lat, center.lng, this.selectedRadiusKm(), true);
  }

  private isOpenAndAvailable(station: Station, service: 'all' | 'charging' | 'swap'): boolean {
    if (station.operatingStatus !== 'Open') return false;
    if (service === 'charging') return station.offersCarCharging && (station.availableChargingBays ?? 0) > 0;
    if (service === 'swap') return station.offersBatterySwap && (station.availableBatteries ?? 0) > 0;
    return (station.offersCarCharging && (station.availableChargingBays ?? 0) > 0) ||
      (station.offersBatterySwap && (station.availableBatteries ?? 0) > 0);
  }

  onSearchChange(value: string) {
    this.searchQuery.set(value);
    this.placeResults.set([]);
    this.placeSearchError.set('');
  }

  submitSearch() {
    const query = this.searchQuery().trim();
    if (query) this.searchStations(query, true);
    if (query.length < 2) return;

    this.placeSearchLoading.set(true);
    this.placeSearchError.set('');
    this.placeSearch.search(query).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (results) => {
        this.placeResults.set(results);
        this.placeSearchLoading.set(false);
      },
      error: () => {
        this.placeSearchError.set('Không thể tìm địa danh. Hãy thử kéo bản đồ đến khu vực cần tìm.');
        this.placeSearchLoading.set(false);
      },
    });
  }

  private searchStations(query: string, reset: boolean) {
    this.stationRequest?.unsubscribe();
    this.globalQuery = query;
    if (reset) { this.page = 0; this.stations.set([]); this.total.set(0); }
    this.loading.set(true);
    this.error.set('');
    this.stationRequest = this.api.search(query, this.page + 1).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result) => {
        const merged = new Map(this.stations().map((station) => [station.id, station]));
        for (const station of result.items) merged.set(station.id, station);
        this.stations.set([...merged.values()]);
        this.page = result.page;
        this.total.set(result.totalCount);
        this.hasMore.set(result.page * result.pageSize < result.totalCount);
        this.loading.set(false);
      },
      error: () => { this.error.set('Không thể tìm trạm. Vui lòng thử lại.'); this.loading.set(false); },
    });
  }

  selectPlace(place: PlaceSearchResult) {
    this.searchCenter.set({ lat: place.latitude, lng: place.longitude });
    this.searchQuery.set('');
    this.placeResults.set([]);
    this.searchNearby(place.latitude, place.longitude, this.selectedRadiusKm(), true);
  }

  toggleSidebar() {
    this.sidebarCollapsed.update((v) => !v);
    if (typeof window !== 'undefined' && window.innerWidth <= 768 && this.sidebarCollapsed()) {
      this.mobileView.set('map');
    }
  }

  openStationList() {
    this.sidebarCollapsed.set(false);
    this.mobileView.set('list');
  }

  copyAddress(address: string) {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(address);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    }
  }

  onListScroll(event: Event) {
    const el = event.target as HTMLElement;
    if (!el) return;
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - 160) {
      if (this.hasMore() && !this.loading()) {
        this.loadMore();
      }
    }
  }

  loadMore() {
    if (this.loading() || (!this.hasMore() && !this.error())) return;
    if (this.globalQuery) { this.searchStations(this.globalQuery, false); return; }
    const center = this.searchCenter();
    if (center) {
      this.searchNearby(center.lat, center.lng, this.selectedRadiusKm(), false);
      return;
    }
  }

  selectStation(id: string) {
    if (typeof window !== 'undefined' && window.innerWidth < 1050) {
      this.sidebarCollapsed.set(true);
      if (window.innerWidth <= 768) setTimeout(() => this.mobileView.set('map'), 0);
    }
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
