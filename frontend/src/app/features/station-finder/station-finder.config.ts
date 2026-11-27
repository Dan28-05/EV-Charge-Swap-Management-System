import { InjectionToken } from '@angular/core';

export interface StationFinderConfig {
  apiUrl: string;
  tileUrl: string;
  subdomains: string[];
  placeSearchUrl: string;
}

export const STATION_FINDER_CONFIG = new InjectionToken<StationFinderConfig>(
  'Station finder configuration',
  {
    providedIn: 'root',
    factory: () => ({
      apiUrl: '/api',
      tileUrl: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
      subdomains: ['a', 'b', 'c'],
      placeSearchUrl: 'https://nominatim.openstreetmap.org/search',
    }),
  },
);
