export interface Station {
  id: string;
  name: string;
  address: string;
  latitude: number;
  longitude: number;
  offersCarCharging: boolean;
  offersBatterySwap: boolean;
  distanceKm?: number;
}

export interface StationPage {
  items: Station[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface NearbyLocationItem {
  location: Station;
  distanceKm: number;
}

export interface NearbyLocationPage {
  items: NearbyLocationItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export function directionsUrl(station: Station): string {
  return `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(`${station.latitude},${station.longitude}`)}`;
}

export function serviceLabel(station: Station): string {
  return [
    station.offersCarCharging ? 'Sạc ô tô' : '',
    station.offersBatterySwap ? 'Đổi pin xe máy' : '',
  ]
    .filter(Boolean)
    .join(' · ');
}

export function formatDistance(distanceKm?: number): string {
  if (distanceKm == null || isNaN(distanceKm)) return '';
  if (distanceKm < 1) {
    return `${Math.round(distanceKm * 1000)} m`;
  }
  return `${distanceKm.toFixed(1)} km`;
}
