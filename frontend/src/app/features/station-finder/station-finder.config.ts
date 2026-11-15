import { InjectionToken } from '@angular/core';

export interface StationFinderConfig {
  apiUrl: string;
  tileUrl: string;
  subdomains: string[];
}

export const STATION_FINDER_CONFIG = new InjectionToken<StationFinderConfig>(
  'Station finder configuration',
  {
    providedIn: 'root',
    factory: () => ({
      apiUrl: '/api',
      tileUrl: 'https://mt{s}.google.com/vt/lyrs=m&x={x}&y={y}&z={z}',
      subdomains: ['0', '1', '2', '3'],
    }),
  },
);
