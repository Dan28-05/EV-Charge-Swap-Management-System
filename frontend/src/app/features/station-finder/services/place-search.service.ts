import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { PlaceSearchResult } from '../models/place-search.model';
import { STATION_FINDER_CONFIG } from '../station-finder.config';

interface NominatimPlace {
  place_id: number;
  display_name: string;
  lat: string;
  lon: string;
}

@Injectable({ providedIn: 'root' })
export class PlaceSearchService {
  private readonly http = inject(HttpClient);
  private readonly url = inject(STATION_FINDER_CONFIG).placeSearchUrl;

  search(query: string): Observable<PlaceSearchResult[]> {
    return this.http.get<NominatimPlace[]>(this.url, {
      params: {
        q: query.trim(), format: 'jsonv2', limit: 5, countrycodes: 'vn', 'accept-language': 'vi',
      },
    }).pipe(map((results) => results.map((place) => ({
      id: String(place.place_id), label: place.display_name,
      latitude: Number(place.lat), longitude: Number(place.lon),
    })).filter((place) => Number.isFinite(place.latitude) && Number.isFinite(place.longitude))));
  }
}
