import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { IncidentResolution, OperatorService } from './operator.service';
import {
  BatteryLifecycleState,
  ChargerEquipmentState,
  OperatorStationDashboardData,
} from '../models/operator.models';

describe('OperatorService', () => {
  let service: OperatorService;
  let httpMock: HttpTestingController;

  const mockStationData: OperatorStationDashboardData = {
    location: {
      id: 'loc-1',
      name: 'Trạm Vincom Đồng Khởi',
      address: '72 Lê Thánh Tôn, Q1, TP.HCM',
      operatingStatus: 'Open',
      offersCarCharging: true,
      offersBatterySwap: true,
    },
    bays: [
      {
        id: 'bay-1',
        locationId: 'loc-1',
        code: 'BAY-01',
        powerKw: 60,
        equipmentState: ChargerEquipmentState.Active,
        parkingState: 1,
      },
    ],
    batteries: [
      {
        id: 'bat-1',
        locationId: 'loc-1',
        serial: 'BAT-001',
        state: BatteryLifecycleState.Ready,
      },
    ],
    hours: [
      {
        id: 'h-1',
        locationId: 'loc-1',
        serviceType: 1,
        day: 1,
        opensAtMinute: 0,
        closesAtMinute: 1440,
      },
    ],
    reservations: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [OperatorService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(OperatorService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should fetch station dashboard data successfully', () => {
    service.getStationDashboard().subscribe((data) => {
      expect(data.location.name).toBe('Trạm Vincom Đồng Khởi');
      expect(data.bays.length).toBe(1);
      expect(data.batteries.length).toBe(1);
    });

    const req = httpMock.expectOne('/api/operator/me/station');
    expect(req.request.method).toBe('GET');
    req.flush(mockStationData);
  });

  it('should patch charging bay equipment state', () => {
    service.updateBayEquipmentState('bay-1', ChargerEquipmentState.Faulted).subscribe();

    const req = httpMock.expectOne('/api/operator/me/charging-bays/bay-1/equipment-state');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ state: ChargerEquipmentState.Faulted });
    req.flush(null);
  });

  it('should patch battery state', () => {
    service.updateBatteryState('bat-1', BatteryLifecycleState.Charging).subscribe();

    const req = httpMock.expectOne('/api/operator/me/batteries/bat-1/state');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ state: BatteryLifecycleState.Charging });
    req.flush(null);
  });

  it('sends the fault description and requires a separate resolution request', () => {
    service.updateBatteryState('bat-1', BatteryLifecycleState.Faulted, 'Pin lỗi').subscribe();
    const fault = httpMock.expectOne('/api/operator/me/batteries/bat-1/state');
    expect(fault.request.body).toEqual({ state: BatteryLifecycleState.Faulted, description: 'Pin lỗi' });
    fault.flush(null);

    service.getIncidentOptions('incident-1').subscribe();
    const options = httpMock.expectOne('/api/operator/me/incidents/incident-1/options');
    expect(options.request.method).toBe('GET');
    options.flush({ canChangeReservation: true, candidates: [{ id: 'bat-2', code: 'PIN-2' }] });

    service.resolveIncident('incident-1', IncidentResolution.Reassigned, 'bat-2').subscribe();
    const resolution = httpMock.expectOne('/api/operator/me/incidents/incident-1/resolve');
    expect(resolution.request.method).toBe('POST');
    expect(resolution.request.body).toEqual({ resolution: IncidentResolution.Reassigned, replacementId: 'bat-2' });
    resolution.flush(null);
  });

  it('should post vehicle-arrived event and refresh dashboard', () => {
    service.confirmVehicleArrived('res-1').subscribe();

    const req1 = httpMock.expectOne('/api/operator/me/charging-reservations/res-1/vehicle-arrived');
    expect(req1.request.method).toBe('POST');
    req1.flush(null);

    const req2 = httpMock.expectOne('/api/operator/me/station');
    expect(req2.request.method).toBe('GET');
    req2.flush(mockStationData);
  });

  it('should start charging session with initial meter reading and refresh dashboard', () => {
    service.startCharging('res-1', 1500.5).subscribe();

    const req1 = httpMock.expectOne('/api/operator/me/charging-reservations/res-1/start');
    expect(req1.request.method).toBe('POST');
    expect(req1.request.body).toEqual({ meterKwh: 1500.5 });
    req1.flush({
      id: 'session-1',
      reservationId: 'res-1',
      chargingBayId: 'bay-1',
      startMeterKwh: 1500.5,
      startedAtUtc: new Date().toISOString(),
      startedById: 'op-1',
    });

    const req2 = httpMock.expectOne('/api/operator/me/station');
    expect(req2.request.method).toBe('GET');
    req2.flush(mockStationData);
  });

  it('should complete charging session with end meter reading and refresh dashboard', () => {
    service.completeCharging('res-1', 1530.2).subscribe();

    const req1 = httpMock.expectOne('/api/operator/me/charging-reservations/res-1/complete');
    expect(req1.request.method).toBe('POST');
    expect(req1.request.body).toEqual({ meterKwh: 1530.2 });
    req1.flush(null);

    const req2 = httpMock.expectOne('/api/operator/me/station');
    expect(req2.request.method).toBe('GET');
    req2.flush(mockStationData);
  });
});
