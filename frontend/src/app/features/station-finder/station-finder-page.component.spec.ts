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
  @Input() userLocation: { lat: number; lng: number } | null = null;
  @Input() radiusKm: number | null = null;
  @Output() stationSelected = new EventEmitter<string>();
  @Output() userLocated = new EventEmitter<{ lat: number; lng: number }>();
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
  it('deduplicates pages and passes the identical collection to list and map', () => {
    const f = setup();
    http
      .expectOne('/api/locations?page=1&pageSize=20')
      .flush({ items: [station('a')], page: 1, pageSize: 1, totalCount: 3 });
    f.componentInstance.loadMore();
    http
      .expectOne('/api/locations?page=2&pageSize=20')
      .flush({ items: [station('a'), station('b')], page: 2, pageSize: 2, totalCount: 3 });
    f.detectChanges();
    expect(f.componentInstance.stations().map((s) => s.id)).toEqual(['a', 'b']);
    expect(f.nativeElement.querySelectorAll('.station-card').length).toBe(2);
    expect(f.debugElement.query(By.directive(MapStub)).componentInstance.stations).toEqual(
      f.componentInstance.stations(),
    );
    expect(f.componentInstance.hasMore()).toBe(false);
  });
  it('shares card/marker selection, cancels stale details, and builds destination coordinates', () => {
    const f = setup();
    http
      .expectOne('/api/locations?page=1&pageSize=20')
      .flush({ items: [station('a'), station('b')], page: 1, pageSize: 20, totalCount: 2 });
    f.detectChanges();
    f.nativeElement.querySelector('.choose').click();
    const first = http.expectOne('/api/locations/a');
    f.debugElement.query(By.directive(MapStub)).componentInstance.stationSelected.emit('b');
    expect(first.cancelled).toBe(true);
    http.expectOne('/api/locations/b').flush(station('b'));
    f.detectChanges();
    expect(f.componentInstance.selectedId()).toBe('b');
    expect(f.nativeElement.querySelector('.selected strong').textContent).toContain('Trạm b');
    const link = f.nativeElement.querySelector('.directions') as HTMLAnchorElement;
    expect(new URL(link.href).searchParams.get('destination')).toBe('13.766667,109.233333');
    expect(link.target).toBe('_blank');
  });
  it('shows an API error, retries the same page, and handles an empty response', () => {
    const f = setup();
    http
      .expectOne('/api/locations?page=1&pageSize=20')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    f.detectChanges();
    expect(f.nativeElement.querySelector('[role=alert]').textContent).toContain('Không thể tải');
    f.nativeElement.querySelector('.error button').click();
    http
      .expectOne('/api/locations?page=1&pageSize=20')
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    f.detectChanges();
    expect(f.nativeElement.querySelector('.empty').textContent).toContain('Chưa có địa điểm');
  });
  it('retains existing stations when a later page fails and retries details', () => {
    const f = setup();
    http
      .expectOne('/api/locations?page=1&pageSize=20')
      .flush({ items: [station('a')], page: 1, pageSize: 20, totalCount: 30 });
    f.componentInstance.loadMore();
    http
      .expectOne('/api/locations?page=2&pageSize=20')
      .flush({}, { status: 500, statusText: 'Error' });
    expect(f.componentInstance.stations().length).toBe(1);
    f.componentInstance.selectStation('a');
    http.expectOne('/api/locations/a').flush({}, { status: 404, statusText: 'Not Found' });
    f.detectChanges();
    expect(f.nativeElement.querySelector('.detail [role=alert]')).toBeTruthy();
    f.componentInstance.selectStation('a');
    http.expectOne('/api/locations/a').flush(station('a'));
    expect(f.componentInstance.detail()?.id).toBe('a');
  });
});
