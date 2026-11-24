import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminService } from './admin.service';

describe('AdminService', () => {
  let service: AdminService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('fetches admin locations and station resources', () => {
    service.listLocations(1, 50).subscribe();
    const listReq = httpMock.expectOne('/api/locations/admin?page=1&pageSize=50');
    expect(listReq.request.method).toBe('GET');
    listReq.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });

    service.getLocationResources('loc-1').subscribe();
    const resReq = httpMock.expectOne('/api/admin/locations/loc-1/resources');
    expect(resReq.request.method).toBe('GET');
    resReq.flush({});
  });

  it('calls batch provisioning and service-hours preset endpoints', () => {
    service.batchCreateBays('loc-1', { prefix: 'BAY-', count: 4, startNumber: 1, powerKw: 150 }).subscribe();
    const bayReq = httpMock.expectOne('/api/admin/locations/loc-1/charging-bays/batch');
    expect(bayReq.request.method).toBe('POST');
    expect(bayReq.request.body.count).toBe(4);
    bayReq.flush({ createdCount: 4, skippedCount: 0, createdCodes: ['BAY-01', 'BAY-02', 'BAY-03', 'BAY-04'] });

    service.batchCreateBatteries('loc-1', { prefix: 'BAT-', count: 10, startNumber: 1 }).subscribe();
    const batReq = httpMock.expectOne('/api/admin/locations/loc-1/batteries/batch');
    expect(batReq.request.method).toBe('POST');
    batReq.flush({ createdCount: 10, skippedCount: 0, createdCodes: [] });

    service.applyServiceHoursPreset('loc-1', { opensAtMinute: 0, closesAtMinute: 1440 }).subscribe();
    const hoursReq = httpMock.expectOne('/api/admin/locations/loc-1/service-hours/preset');
    expect(hoursReq.request.method).toBe('PUT');
    hoursReq.flush(null);
  });

  it('passes report summary query parameters cleanly', () => {
    service
      .getReportsSummary({
        locationId: 'loc-1',
        fromUtc: '2026-11-01T00:00:00Z',
        toUtc: '2026-11-24T23:59:59Z',
      })
      .subscribe();

    const req = httpMock.expectOne(
      '/api/admin/reports/summary?locationId=loc-1&fromUtc=2026-11-01T00:00:00Z&toUtc=2026-11-24T23:59:59Z',
    );
    expect(req.request.method).toBe('GET');
    req.flush({});
  });
});
