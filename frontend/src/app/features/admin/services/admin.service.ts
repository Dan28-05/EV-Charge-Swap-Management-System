import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Station } from '../../station-finder/models/station.model';
import {
  AdminLocationResources,
  AdminOperatorSummary,
  AdminReportsSummary,
  BatchCreateBatteriesPayload,
  BatchCreateBaysPayload,
  BatchProvisionResult,
  CreateOperatorPayload,
  LocationUpsertPayload,
  OperatorAssignmentRecord,
  PagedAdminLocations,
  ServiceHoursPresetPayload,
} from '../models/admin.models';

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  listLocations(page = 1, pageSize = 100): Observable<PagedAdminLocations> {
    return this.http.get<PagedAdminLocations>('/api/locations/admin', {
      params: { page, pageSize },
    });
  }

  createLocation(payload: LocationUpsertPayload): Observable<Station> {
    return this.http.post<Station>('/api/locations', payload);
  }

  updateLocation(id: string, payload: LocationUpsertPayload): Observable<Station> {
    return this.http.put<Station>(`/api/locations/${id}`, payload);
  }

  setVisibility(id: string, isActive: boolean): Observable<void> {
    return this.http.patch<void>(`/api/locations/${id}/visibility`, { isActive });
  }

  rebuildSearchIndex(): Observable<void> {
    return this.http.post<void>('/api/locations/admin/search/rebuild', {});
  }

  getLocationResources(locationId: string): Observable<AdminLocationResources> {
    return this.http.get<AdminLocationResources>(`/api/admin/locations/${locationId}/resources`);
  }

  batchCreateBays(locationId: string, payload: BatchCreateBaysPayload): Observable<BatchProvisionResult> {
    return this.http.post<BatchProvisionResult>(
      `/api/admin/locations/${locationId}/charging-bays/batch`,
      payload,
    );
  }

  batchCreateBatteries(locationId: string, payload: BatchCreateBatteriesPayload): Observable<BatchProvisionResult> {
    return this.http.post<BatchProvisionResult>(
      `/api/admin/locations/${locationId}/batteries/batch`,
      payload,
    );
  }

  applyServiceHoursPreset(locationId: string, payload: ServiceHoursPresetPayload): Observable<void> {
    return this.http.put<void>(`/api/admin/locations/${locationId}/service-hours/preset`, payload);
  }

  listOperators(): Observable<AdminOperatorSummary[]> {
    return this.http.get<AdminOperatorSummary[]>('/api/admin/operators');
  }

  createOperator(payload: CreateOperatorPayload): Observable<AdminOperatorSummary> {
    return this.http.post<AdminOperatorSummary>('/api/users/operators', payload);
  }

  assignOperator(operatorId: string, locationId: string): Observable<OperatorAssignmentRecord> {
    return this.http.put<OperatorAssignmentRecord>(`/api/operators/${operatorId}/assignment`, {
      locationId,
    });
  }

  endOperatorAssignment(operatorId: string): Observable<void> {
    return this.http.delete<void>(`/api/operators/${operatorId}/assignment`);
  }

  getOperatorAssignmentHistory(operatorId: string): Observable<OperatorAssignmentRecord[]> {
    return this.http.get<OperatorAssignmentRecord[]>(`/api/operators/${operatorId}/assignments`);
  }

  getReportsSummary(options?: {
    locationId?: string | null;
    fromUtc?: string | null;
    toUtc?: string | null;
  }): Observable<AdminReportsSummary> {
    let params = new HttpParams();
    if (options?.locationId) params = params.set('locationId', options.locationId);
    if (options?.fromUtc) params = params.set('fromUtc', options.fromUtc);
    if (options?.toUtc) params = params.set('toUtc', options.toUtc);
    return this.http.get<AdminReportsSummary>('/api/admin/reports/summary', { params });
  }
}
