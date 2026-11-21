import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, forkJoin, map, of, switchMap, tap, throwError } from 'rxjs';
import {
  BatteryLifecycleState,
  ChargerEquipmentState,
  OperatorActiveReservation,
  OperatorChargingSessionInfo,
  OperatorStationDashboardData,
  ReplaceHoursRequest,
} from '../models/operator.models';
import { CheckInRequest, ReservationResponse } from '../../reservations/models/reservation.models';

export interface ChargingReservationStatusResponse {
  reservationId: string;
  chargingBayId: string;
  status: string;
  vehicleArrivedAtUtc?: string | null;
  noChargeConfirmedAtUtc?: string | null;
  vehicleDepartedAtUtc?: string | null;
  session?: OperatorChargingSessionInfo | null;
}

export interface StationIncident {
  id: string;
  chargingBayId: string | null;
  batteryId: string | null;
  reservationId: string | null;
  chargingSessionId: string | null;
  description: string;
  reportedAtUtc: string;
}

export interface IncidentOptions {
  canChangeReservation: boolean;
  candidates: { id: string; code: string }[];
}

export enum IncidentResolution {
  Reassigned = 1,
  Cancelled,
  Recorded,
}

export interface StationTariff {
  id: string;
  serviceType: number;
  unitPrice: number;
  effectiveFromUtc: string;
}

@Injectable({ providedIn: 'root' })
export class OperatorService {
  private readonly http = inject(HttpClient);

  readonly stationData = signal<OperatorStationDashboardData | null>(null);
  readonly loading = signal<boolean>(false);
  readonly error = signal<string | null>(null);

  /**
   * Load station data including bays, batteries, hours and active reservations.
   * Also hydrates detailed charging status for checked-in charging reservations.
   */
  getStationDashboard(): Observable<OperatorStationDashboardData> {
    this.loading.set(true);
    this.error.set(null);

    return this.http.get<OperatorStationDashboardData>('/api/operator/me/station').pipe(
      switchMap((raw) => {
        // Normalize status enum to string
        const normalizedReservations: OperatorActiveReservation[] = (raw.reservations || []).map((r) => {
          let s: any = r.status;
          if (s === 1) s = 'Held';
          else if (s === 2) s = 'CheckedIn';
          else if (s === 3) s = 'Completed';
          else if (s === 4) s = 'Cancelled';
          else if (s === 5) s = 'Expired';
          return { ...r, status: s };
        });

        const data: OperatorStationDashboardData = {
          ...raw,
          reservations: normalizedReservations,
        };

        // Hydrate charging reservation details if any charging bay reservations are checked in
        const chargingReservations = (data.reservations || []).filter(
          (r) => !!r.chargingBayId && r.status === 'CheckedIn'
        );

        if (chargingReservations.length === 0) {
          this.stationData.set(data);
          this.loading.set(false);
          return of(data);
        }

        const detailRequests = chargingReservations.map((r) =>
          this.getChargingStatus(r.id).pipe(
            map((status) => ({ id: r.id, status })),
            catchError(() => of({ id: r.id, status: null }))
          )
        );

        return forkJoin(detailRequests).pipe(
          map((details) => {
            const detailMap = new Map(details.map((d) => [d.id, d.status]));
            const enrichedReservations: OperatorActiveReservation[] = (data.reservations || []).map((r) => {
              const detail = detailMap.get(r.id);
              if (detail) {
                return {
                  ...r,
                  vehicleArrivedAtUtc: detail.vehicleArrivedAtUtc,
                  noChargeConfirmedAtUtc: detail.noChargeConfirmedAtUtc,
                  vehicleDepartedAtUtc: detail.vehicleDepartedAtUtc,
                  session: detail.session,
                };
              }
              return r;
            });

            const enrichedData: OperatorStationDashboardData = {
              ...data,
              reservations: enrichedReservations,
            };
            this.stationData.set(enrichedData);
            this.loading.set(false);
            return enrichedData;
          })
        );
      }),
      catchError((err: HttpErrorResponse) => {
        this.loading.set(false);
        const msg = err.status === 404
          ? 'Tài khoản chưa được phân công quản lý trạm nào.'
          : (err.error?.detail || err.error?.message || 'Không thể tải dữ liệu trạm.');
        this.error.set(msg);
        return throwError(() => err);
      })
    );
  }

  getOpenIncidents(page: number): Observable<StationIncident[]> {
    return this.http.get<StationIncident[]>('/api/operator/me/incidents', { params: { page } });
  }

  getTariffs(): Observable<StationTariff[]> {
    return this.http.get<StationTariff[]>('/api/operator/me/tariffs');
  }

  createTariff(serviceType: number, unitPrice: number): Observable<StationTariff> {
    return this.http.post<StationTariff>('/api/operator/me/tariffs', { serviceType, unitPrice });
  }

  completeBatterySwap(id: string, returnedSerial: string, issuedSerial: string, inspectionResult: string): Observable<unknown> {
    return this.http.post(`/api/operator/me/battery-swaps/${id}/complete`,
      { returnedSerial, issuedSerial, inspectionResult });
  }

  getIncidentOptions(id: string): Observable<IncidentOptions> {
    return this.http.get<IncidentOptions>(`/api/operator/me/incidents/${id}/options`);
  }

  resolveIncident(id: string, resolution: IncidentResolution, replacementId?: string): Observable<void> {
    return this.http.post<void>(`/api/operator/me/incidents/${id}/resolve`, { resolution, replacementId });
  }

  updateBayEquipmentState(bayId: string, state: ChargerEquipmentState, description?: string): Observable<void> {
    const request = description === undefined ? { state } : { state, description };
    return this.http.patch<void>(`/api/operator/me/charging-bays/${bayId}/equipment-state`, request).pipe(
      tap(() => {
        // Optimistic local update
        const current = this.stationData();
        if (current) {
          const updatedBays = current.bays.map((b) =>
            b.id === bayId ? { ...b, equipmentState: state, stateUpdatedAtUtc: new Date().toISOString() } : b
          );
          this.stationData.set({ ...current, bays: updatedBays });
        }
      })
    );
  }

  updateBatteryState(batteryId: string, state: BatteryLifecycleState, description?: string): Observable<void> {
    const request = description === undefined ? { state } : { state, description };
    return this.http.patch<void>(`/api/operator/me/batteries/${batteryId}/state`, request).pipe(
      tap(() => {
        // Optimistic local update
        const current = this.stationData();
        if (current) {
          const updatedBatteries = current.batteries.map((b) =>
            b.id === batteryId ? { ...b, state, stateUpdatedAtUtc: new Date().toISOString() } : b
          );
          this.stationData.set({ ...current, batteries: updatedBatteries });
        }
      })
    );
  }

  updateServiceHours(request: ReplaceHoursRequest): Observable<void> {
    return this.http.put<void>('/api/operator/me/service-hours', request).pipe(
      switchMap(() => this.getStationDashboard().pipe(map(() => void 0)))
    );
  }

  getChargingStatus(reservationId: string): Observable<ChargingReservationStatusResponse> {
    return this.http.get<ChargingReservationStatusResponse>(
      `/api/operator/me/charging-reservations/${reservationId}`
    );
  }

  confirmVehicleArrived(reservationId: string): Observable<void> {
    return this.http
      .post<void>(`/api/operator/me/charging-reservations/${reservationId}/vehicle-arrived`, {})
      .pipe(switchMap(() => this.getStationDashboard().pipe(map(() => void 0))));
  }

  startCharging(reservationId: string, meterKwh: number): Observable<OperatorChargingSessionInfo> {
    return this.http
      .post<OperatorChargingSessionInfo>(`/api/operator/me/charging-reservations/${reservationId}/start`, {
        meterKwh,
      })
      .pipe(switchMap((session) => this.getStationDashboard().pipe(map(() => session))));
  }

  completeCharging(reservationId: string, meterKwh: number): Observable<void> {
    return this.http
      .post<void>(`/api/operator/me/charging-reservations/${reservationId}/complete`, {
        meterKwh,
      })
      .pipe(switchMap(() => this.getStationDashboard().pipe(map(() => void 0))));
  }

  confirmNoCharge(reservationId: string): Observable<void> {
    return this.http
      .post<void>(`/api/operator/me/charging-reservations/${reservationId}/no-charge`, {})
      .pipe(switchMap(() => this.getStationDashboard().pipe(map(() => void 0))));
  }

  confirmVehicleDeparted(reservationId: string): Observable<void> {
    return this.http
      .post<void>(`/api/operator/me/charging-reservations/${reservationId}/vehicle-departed`, {})
      .pipe(switchMap(() => this.getStationDashboard().pipe(map(() => void 0))));
  }

  checkIn(request: CheckInRequest): Observable<ReservationResponse> {
    return this.http.post<ReservationResponse>('/api/reservations/check-in', request).pipe(
      switchMap((res) => this.getStationDashboard().pipe(map(() => res)))
    );
  }

  closeWithoutService(reservationId: string, locationId: string): Observable<void> {
    return this.http
      .post<void>(
        `/api/reservations/${reservationId}/close-without-service?locationId=${encodeURIComponent(locationId)}`,
        {}
      )
      .pipe(switchMap(() => this.getStationDashboard().pipe(map(() => void 0))));
  }
}
