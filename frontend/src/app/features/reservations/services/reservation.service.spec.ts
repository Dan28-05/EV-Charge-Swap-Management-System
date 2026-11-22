import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ReservationService } from './reservation.service';
import { ReservationResponse, ReservationServiceType } from '../models/reservation.models';
import { ReservationRealtimeService } from './reservation-realtime.service';
import { AuthService } from '../../auth/services/auth.service';
import { Subject } from 'rxjs';
import { vi } from 'vitest';

describe('ReservationService', () => {
  let service: ReservationService;
  let httpMock: HttpTestingController;
  let updates: Subject<string | null>;

  const mockReservation: ReservationResponse = {
    id: 'res-1111',
    locationId: 'loc-2222',
    chargingBayId: 'bay-3333',
    batteryId: null,
    status: 'Held',
    code: '86DAAC0F5183AD453962BC8D9B8DDF97',
    expiresAtUtc: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
    checkedInAtUtc: null,
    closingReason: null,
  };

  beforeEach(() => {
    updates = new Subject<string | null>();
    TestBed.configureTestingModule({
      providers: [
        ReservationService,
        { provide: AuthService, useValue: { isAuthenticated: () => true, currentUser: () => ({ id: 'customer-1' }) } },
        { provide: ReservationRealtimeService, useValue: { changed$: updates, connected: () => true } },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(ReservationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('refreshes from the API on reconnect and exposes the closed state when the active hold disappears', async () => {
    vi.useFakeTimers();
    try {
      service.setActive(mockReservation);
      updates.next(null);
      await vi.advanceTimersByTimeAsync(100);
      httpMock.expectOne('/api/reservations/active').flush(null, { status: 404, statusText: 'Not Found' });
      httpMock.expectOne('/api/reservations/res-1111').flush({
        ...mockReservation, status: 'Cancelled', code: null, closingReason: 'RESOURCE_FAULT',
      });
      expect(service.activeReservation()).toBeNull();
      expect(service.lastClosedReservation()?.closingReason).toBe('RESOURCE_FAULT');
    } finally {
      service.setActive(null);
      vi.useRealTimers();
    }
  });

  it('holds a bay or battery and sets activeReservation signal', () => {
    service.hold({ locationId: 'loc-2222', serviceType: ReservationServiceType.Charging }).subscribe((res) => {
      expect(res.id).toBe('res-1111');
      expect(res.status).toBe('Held');
    });

    const req = httpMock.expectOne('/api/reservations');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ locationId: 'loc-2222', serviceType: 1 });
    expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/i);
    req.flush(mockReservation);

    expect(service.activeReservation()?.id).toBe('res-1111');
    expect(service.isHeld()).toBe(true);
  });

  it('handles getActive returning active reservation', () => {
    service.getActive().subscribe((res) => {
      expect(res?.id).toBe('res-1111');
    });

    const req = httpMock.expectOne('/api/reservations/active');
    expect(req.request.method).toBe('GET');
    req.flush(mockReservation);

    expect(service.hasActiveReservation()).toBe(true);
  });

  it('handles getActive returning 404 as null reservation gracefully', () => {
    service.getActive().subscribe((res) => {
      expect(res).toBeNull();
    });

    const req = httpMock.expectOne('/api/reservations/active');
    req.flush({ status: 404, title: 'Not Found' }, { status: 404, statusText: 'Not Found' });

    expect(service.activeReservation()).toBeNull();
    expect(service.hasActiveReservation()).toBe(false);
  });

  it('cancels active reservation and clears signal', () => {
    service.setActive(mockReservation);
    expect(service.activeReservation()).not.toBeNull();

    service.cancel('res-1111').subscribe();

    const req = httpMock.expectOne('/api/reservations/res-1111/cancel');
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(service.activeReservation()).toBeNull();
  });

  it('operator checks in customer with 32-character code', () => {
    const checkedInResponse = { ...mockReservation, status: 'CheckedIn', checkedInAtUtc: new Date().toISOString() };

    service.checkIn({ locationId: 'loc-2222', code: '86DAAC0F5183AD453962BC8D9B8DDF97' }).subscribe((res) => {
      expect(res.status).toBe('CheckedIn');
    });

    const req = httpMock.expectOne('/api/reservations/check-in');
    expect(req.request.method).toBe('POST');
    req.flush(checkedInResponse);
  });

  it('operator closes checked-in reservation without service', () => {
    service.closeWithoutService('res-1111', 'loc-2222').subscribe();

    const req = httpMock.expectOne('/api/reservations/res-1111/close-without-service?locationId=loc-2222');
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('keeps activeReservation and enables polling when reservation is CheckedIn', () => {
    const checkedInRes: ReservationResponse = {
      ...mockReservation,
      status: 'CheckedIn',
      checkedInAtUtc: new Date().toISOString(),
    };

    service.setActive(checkedInRes);
    expect(service.isCheckedIn()).toBe(true);
    expect(service.hasActiveReservation()).toBe(true);

    // When backend returns 404 (service completed/departed)
    service.getActive().subscribe();
    const req = httpMock.expectOne('/api/reservations/active');
    req.flush({ status: 404, title: 'Not Found' }, { status: 404, statusText: 'Not Found' });

    expect(service.activeReservation()).toBeNull();
    expect(service.hasActiveReservation()).toBe(false);
    expect(service.isCheckedIn()).toBe(false);
  });
});
