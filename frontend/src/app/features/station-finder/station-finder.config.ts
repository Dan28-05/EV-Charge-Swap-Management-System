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
      tileUrl: 'https://{s}.tile.openstreetmap.fr/hot/{z}/{x}/{y}.png',
      subdomains: ['a', 'b'],
      placeSearchUrl: 'https://nominatim.openstreetmap.org/search',
    }),
  },
);
