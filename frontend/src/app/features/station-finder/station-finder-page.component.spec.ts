import { Component, EventEmitter, Input, Output } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { StationFinderPageComponent } from './station-finder-page.component';
import { StationMapComponent } from './station-map/station-map.component';
import { Station } from './models/station.model';

@Component({ selector: 'app-station-map', template: '' })
class MapStub {
  @Input() stations: Station[] = [];
  @Input() selectedId: string | null = null;
  @Input() sidebarCollapsed = false;
  @Input() userLocation: { lat: number; lng: number } | null = null;
  @Input() searchCenter: { lat: number; lng: number } | null = null;
  @Input() radiusKm: number | null = null;
  @Output() stationSelected = new EventEmitter<string>();
  @Output() userLocated = new EventEmitter<{ lat: number; lng: number }>();
  @Output() mapAreaChanged = new EventEmitter<{ lat: number; lng: number }>();
}
const station = (id: string): Station => ({
  id,
  name: `Trạm ${id}`,
  address: 'Quy Nhơn',
  latitude: 13.766667,
  longitude: 109.233333,
  offersCarCharging: true,
  offersBatterySwap: true,
});

describe('Station finder', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StationFinderPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    })
      .overrideComponent(StationFinderPageComponent, {
        remove: { imports: [StationMapComponent] },
        add: { imports: [MapStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function setup() {
    const fixture = TestBed.createComponent(StationFinderPageComponent);
    fixture.detectChanges();
    return fixture;
  }
  function expectNearby() {
    return http.expectOne((request) => request.url === '/api/locations/nearby');
  }

  it('does not load a national list before the customer chooses a search area', () => {
    const f = setup();
    http.expectNone('/api/locations');
    expect(f.componentInstance.stations()).toEqual([]);
    expect(f.nativeElement.querySelector('.empty').textContent).toContain('Kéo bản đồ');
  });

  it('loads and deduplicates stations around the area the customer moves to', () => {
    const f = setup();
    f.componentInstance.onMapAreaChanged({ lat: 13.77, lng: 109.22 });
    expectNearby().flush({
      items: [{ location: station('a'), distanceKm: 0.3 }], page: 1, pageSize: 20, totalCount: 21,
    });
    f.componentInstance.loadMore();
    expectNearby().flush({
      items: [{ location: station('a'), distanceKm: 0.3 }, { location: station('b'), distanceKm: 0.9 }],
      page: 2, pageSize: 20, totalCount: 21,
    });
    expect(f.componentInstance.stations().map((item) => item.id)).toEqual(['a', 'b']);
    expect(f.componentInstance.total()).toBe(21);
  });

  it('sends the selected availability filter to the nearby API', () => {
    const f = setup();
    f.componentInstance.onMapAreaChanged({ lat: 13.77, lng: 109.22 });
    expectNearby().flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    f.componentInstance.setAvailabilityFilter('available');
    const request = expectNearby();
    expect(request.request.params.get('availability')).toBe('available');
    request.flush({
      items: [{ location: { ...station('ready'), operatingStatus: 'Open', availableChargingBays: 1 }, distanceKm: 0.4 }],
      page: 1, pageSize: 20, totalCount: 1,
    });
    expect(f.componentInstance.displayedStations().map((item) => item.id)).toEqual(['ready']);
  });

  it('retries a failed station search even if the preceding area had no more pages', () => {
    const f = setup();
    f.componentInstance.onMapAreaChanged({ lat: 13.77, lng: 109.22 });
    expectNearby().flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    f.componentInstance.onSearchChange('Nha Trang');
    f.componentInstance.submitSearch();
    http.expectOne((request) => request.url === '/api/locations/search')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    http.expectOne((request) => request.url.includes('nominatim.openstreetmap.org'))
      .flush([]);
    expect(f.componentInstance.error()).toContain('Không thể tìm trạm');
    f.componentInstance.loadMore();
    http.expectOne((request) => request.url === '/api/locations/search')
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    expect(f.componentInstance.error()).toBe('');
  });

  it('debounces station suggestions and keeps results matched without Vietnamese accents', async () => {
    const f = setup();
    vi.useFakeTimers();
    try {
      f.componentInstance.onSearchChange('vincom dong');
      await vi.advanceTimersByTimeAsync(160);
      f.componentInstance.onSearchChange('vincom dong kh');
      await vi.advanceTimersByTimeAsync(160);
      http.expectNone((request) => request.url === '/api/locations/search');
      await vi.advanceTimersByTimeAsync(140);
      const request = http.expectOne((req) => req.url === '/api/locations/search');
      expect(request.request.params.get('query')).toBe('vincom dong kh');
      request.flush({
        items: [{ ...station('vincom'), name: 'Vincom Đồng Khởi' }],
        page: 1, pageSize: 20, totalCount: 1,
      });
      http.expectNone((req) => req.url.includes('nominatim.openstreetmap.org'));
      expect(f.componentInstance.displayedStations().map((item) => item.name)).toEqual(['Vincom Đồng Khởi']);
    } finally {
      vi.useRealTimers();
    }
  });
});
