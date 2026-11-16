import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { NearbyLocationPage, Station, StationPage } from '../models/station.model';
import { STATION_FINDER_CONFIG } from '../station-finder.config';

@Injectable({ providedIn: 'root' })
export class LocationsApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(STATION_FINDER_CONFIG).apiUrl.replace(/\/$/, '')}/locations`;

  list(page: number, pageSize = 20): Observable<StationPage> {
    return this.http.get<StationPage>(this.url, { params: { page, pageSize } });
  }

  detail(id: string): Observable<Station> {
    return this.http.get<Station>(`${this.url}/${encodeURIComponent(id)}`);
  }

  search(query: string, page = 1, pageSize = 20): Observable<StationPage> {
    return this.http.get<StationPage>(`${this.url}/search`, { params: { query, page, pageSize } });
  }

  nearby(
    latitude: number,
    longitude: number,
    radiusKm = 10,
    service = 'all',
    availability = 'all',
    search = '',
    page = 1,
    pageSize = 20,
  ): Observable<StationPage> {
    const params: Record<string, string | number> = {
      latitude,
      longitude,
      radiusKm,
      service,
      page,
      pageSize,
    };
    if (availability !== 'all') params['availability'] = availability;
    if (search.trim()) params['search'] = search.trim();

    return this.http
      .get<NearbyLocationPage>(`${this.url}/nearby`, { params })
      .pipe(
        map((res) => ({
          page: res.page,
          pageSize: res.pageSize,
          totalCount: res.totalCount,
          items: res.items.map((item) => ({
            ...item.location,
            distanceKm: item.distanceKm,
          })),
        })),
      );
  }
}
