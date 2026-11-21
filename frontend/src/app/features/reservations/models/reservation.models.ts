export enum ReservationServiceType {
  Charging = 1,
  BatterySwap = 2,
}

export type ReservationStatus = 'Held' | 'CheckedIn' | 'Completed' | 'Cancelled' | 'Expired';

export interface HoldRequest {
  locationId: string;
  serviceType: ReservationServiceType;
}

export interface CheckInRequest {
  locationId: string;
  code: string;
}

export interface ReservationResponse {
  id: string;
  locationId: string;
  chargingBayId: string | null;
  batteryId: string | null;
  status: ReservationStatus | string;
  code: string | null;
  expiresAtUtc: string;
  checkedInAtUtc: string | null;
  closingReason: string | null;
  resourceCode?: string | null;
  chargingStartedAtUtc?: string | null;
  chargingCompletedAtUtc?: string | null;
  invoiceTotalAmount?: number | null;
  invoiceStatus?: string | null;
}
