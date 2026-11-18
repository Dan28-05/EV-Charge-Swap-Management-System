export interface OperatorLocationInfo {
  id: string;
  name: string;
  address: string;
  operatingStatus: 'Unknown' | 'Open' | 'Closed' | 'Maintenance';
  offersCarCharging: boolean;
  offersBatterySwap: boolean;
}

export enum ChargerEquipmentState {
  Active = 1,
  Faulted = 2,
  Maintenance = 3,
}

export enum ParkingOccupancyState {
  Vacant = 1,
  Occupied = 2,
}

export enum BatteryLifecycleState {
  Ready = 1,
  WithCustomer = 2,
  Inspection = 3,
  Charging = 4,
  Faulted = 5,
}

export interface OperatorChargingBay {
  id: string;
  locationId: string;
  code: string;
  powerKw: number;
  equipmentState: ChargerEquipmentState;
  parkingState: ParkingOccupancyState;
  stateUpdatedById?: string;
  stateUpdatedAtUtc?: string;
}

export interface OperatorBattery {
  id: string;
  locationId?: string;
  serial: string;
  state: BatteryLifecycleState;
  stateUpdatedById?: string;
  stateUpdatedAtUtc?: string;
}

export interface OperatorServiceHours {
  id: string;
  locationId: string;
  serviceType: number; // 1: Charging, 2: BatterySwap
  day: number;         // 0: Sunday ... 6: Saturday
  opensAtMinute: number;
  closesAtMinute: number;
  updatedAtUtc?: string;
}

export interface OperatorChargingSessionInfo {
  id: string;
  reservationId: string;
  chargingBayId: string;
  startMeterKwh: number;
  endMeterKwh?: number | null;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  startedById: string;
  completedById?: string | null;
}

export interface OperatorActiveReservation {
  id: string;
  chargingBayId?: string | null;
  batteryId?: string | null;
  status: 'Held' | 'CheckedIn' | 'Completed' | 'Cancelled' | 'Expired';
  expiresAtUtc: string;
  checkedInAtUtc?: string | null;
  // Live session status details if queried
  vehicleArrivedAtUtc?: string | null;
  noChargeConfirmedAtUtc?: string | null;
  vehicleDepartedAtUtc?: string | null;
  session?: OperatorChargingSessionInfo | null;
}

export interface OperatorStationDashboardData {
  location: OperatorLocationInfo;
  bays: OperatorChargingBay[];
  batteries: OperatorBattery[];
  hours: OperatorServiceHours[];
  reservations: OperatorActiveReservation[];
}

export interface ServiceIntervalItem {
  opensAtMinute: number;
  closesAtMinute: number;
}

export interface ReplaceHoursRequest {
  serviceType: number;
  day: number;
  intervals: ServiceIntervalItem[];
}
