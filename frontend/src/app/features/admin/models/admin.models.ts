import { Station } from '../../station-finder/models/station.model';

export type StationOperatingStatus = 'Unknown' | 'Open' | 'Closed' | 'Maintenance';

export interface AdminLocationItem {
  location: Station;
  isActive: boolean;
}

export interface PagedAdminLocations {
  items: AdminLocationItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface LocationUpsertPayload {
  name: string;
  address: string;
  latitude: number;
  longitude: number;
  offersCarCharging: boolean;
  offersBatterySwap: boolean;
  operatingStatus: StationOperatingStatus;
  isActive?: boolean;
  isOpen24Hours: boolean;
  chargingPowerKw: number | null;
  batteryCapacityKwh: number | null;
}

export interface AdminBayItem {
  id: string;
  code: string;
  powerKw: number;
  equipmentState: string;
  parkingState: string;
}

export interface AdminBatteryItem {
  id: string;
  serial: string;
  state: string;
}

export interface AdminServiceHoursItem {
  id: string;
  serviceType: string;
  day: number;
  opensAtMinute: number;
  closesAtMinute: number;
}

export interface AdminAssignedOperatorItem {
  operatorId: string;
  fullName: string;
  email: string;
  phoneNumber: string | null;
  assignedAtUtc: string | null;
}

export interface StationReadinessChecklist {
  hasValidCoordinates: boolean;
  hasRequiredResources: boolean;
  hasServiceHours: boolean;
  hasAssignedOperator: boolean;
  canGoLive: boolean;
}

export interface AdminLocationResources {
  station: AdminLocationItem;
  bays: AdminBayItem[];
  batteries: AdminBatteryItem[];
  serviceHours: AdminServiceHoursItem[];
  assignedOperators: AdminAssignedOperatorItem[];
  readiness: StationReadinessChecklist;
}

export interface BatchCreateBaysPayload {
  prefix: string;
  count: number;
  startNumber: number;
  powerKw: number;
}

export interface BatchCreateBatteriesPayload {
  prefix: string;
  count: number;
  startNumber: number;
}

export interface BatchProvisionResult {
  createdCount: number;
  skippedCount: number;
  createdCodes: string[];
}

export interface ServiceHoursPresetPayload {
  opensAtMinute: number;
  closesAtMinute: number;
}

export interface AdminOperatorSummary {
  id: string;
  fullName: string;
  email: string;
  phoneNumber: string | null;
  isActive: boolean;
  assignedLocationId: string | null;
  assignedLocationName: string | null;
  assignedAtUtc: string | null;
}

export interface CreateOperatorPayload {
  email: string;
  fullName: string;
  phoneNumber?: string | null;
  password: string;
}

export interface OperatorAssignmentRecord {
  id: string;
  operatorId: string;
  locationId: string;
  assignedByAdminId: string | null;
  assignedAtUtc: string | null;
  endedAtUtc: string | null;
}

export interface AdminLocationReportItem {
  locationId: string;
  locationName: string;
  isActive: boolean;
  operatingStatus: string;
  chargingSessionCount: number;
  totalKwh: number;
  batterySwapCount: number;
  paidRevenue: number;
  pendingAmount: number;
  totalBays: number;
  occupiedBays: number;
  faultedBays: number;
  totalBatteries: number;
  readyBatteries: number;
  incidentCount: number;
  openIncidentCount: number;
}

export interface AdminReportsSummary {
  fromUtc: string | null;
  toUtc: string | null;
  totalStations: number;
  activeStations: number;
  chargingSessionCount: number;
  totalKwh: number;
  batterySwapCount: number;
  paidRevenue: number;
  pendingAmount: number;
  overdueInvoiceCount: number;
  totalBays: number;
  occupiedBays: number;
  faultedBays: number;
  bayUtilizationPercent: number;
  incidentCount: number;
  openIncidentCount: number;
  byLocation: AdminLocationReportItem[];
}
