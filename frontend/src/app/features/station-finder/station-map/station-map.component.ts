import {
  AfterViewInit,
  Component,
  ElementRef,
  EventEmitter,
  inject,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  ViewChild,
} from '@angular/core';
import * as L from 'leaflet';
import { serviceLabel, Station } from '../models/station.model';
import { STATION_FINDER_CONFIG } from '../station-finder.config';

@Component({
  selector: 'app-station-map',
  template: `
    <div #canvas class="canvas" aria-label="Bản đồ trạm sạc và đổi pin Google Maps"></div>

    <div class="map-floating-controls" aria-label="Điều khiển bản đồ">
      <div class="control-group zoom-group">
        <button
          type="button"
          class="ctrl-btn"
          aria-label="Phóng to bản đồ"
          title="Phóng to"
          (click)="zoomIn()"
        >
          <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2.5">
            <line x1="12" y1="5" x2="12" y2="19" />
            <line x1="5" y1="12" x2="19" y2="12" />
          </svg>
        </button>
        <div class="divider"></div>
        <button
          type="button"
          class="ctrl-btn"
          aria-label="Thu nhỏ bản đồ"
          title="Thu nhỏ"
          (click)="zoomOut()"
        >
          <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2.5">
            <line x1="5" y1="12" x2="19" y2="12" />
          </svg>
        </button>
      </div>

      <button
        type="button"
        class="ctrl-btn single-btn"
        aria-label="Vị trí của tôi"
        title="Vị trí của tôi"
        [class.active]="locating"
        (click)="locateMe()"
      >
        <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2">
          <circle cx="12" cy="12" r="7" />
          <line x1="12" y1="1" x2="12" y2="5" />
          <line x1="12" y1="19" x2="12" y2="23" />
          <line x1="1" y1="12" x2="5" y2="12" />
          <line x1="19" y1="12" x2="23" y2="12" />
        </svg>
      </button>

      <button
        type="button"
        class="ctrl-btn single-btn"
        aria-label="Xem toàn bộ các trạm"
        title="Xem toàn bộ trạm"
        (click)="fitAll()"
      >
        <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M15 3h6v6M9 21H3v-6M21 9v6m-18-6v6" />
        </svg>
      </button>
    </div>
  `,
  styles: `
    :host {
      display: block;
      position: relative;
      width: 100%;
      height: 100%;
      min-height: 400px;
    }
    .canvas {
      width: 100%;
      height: 100%;
      min-height: 400px;
      background: #e8ece9;
    }
    .map-floating-controls {
      position: absolute;
      right: 20px;
      bottom: 28px;
      z-index: 1000;
      display: flex;
      flex-direction: column;
      gap: 12px;
      align-items: center;
    }
    .control-group {
      background: #ffffff;
      border-radius: 12px;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.16);
      overflow: hidden;
      display: flex;
      flex-direction: column;
      border: 1px solid rgba(0, 0, 0, 0.08);
    }
    .single-btn {
      background: #ffffff;
      border-radius: 12px;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.16);
      border: 1px solid rgba(0, 0, 0, 0.08);
    }
    .ctrl-btn {
      width: 44px;
      height: 44px;
      display: grid;
      place-items: center;
      background: transparent;
      border: none;
      color: #374151;
      cursor: pointer;
      padding: 0;
      transition: all 0.15s ease;
    }
    .ctrl-btn:hover {
      background: #f3f4f6;
      color: #111827;
    }
    .ctrl-btn:active {
      background: #e5e7eb;
      transform: scale(0.96);
    }
    .ctrl-btn.active {
      color: #2563eb;
      background: #eff6ff;
    }
    .divider {
      height: 1px;
      background: #e5e7eb;
      width: 100%;
    }
    @media (max-width: 760px) {
      .map-floating-controls {
        right: 14px;
        bottom: 80px;
        gap: 8px;
      }
      .ctrl-btn {
        width: 38px;
        height: 38px;
      }
    }
  `,
})
export class StationMapComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('canvas', { static: true }) canvas!: ElementRef<HTMLDivElement>;
  @Input() stations: Station[] = [];
  @Input() selectedId: string | null = null;
  @Input() userLocation: { lat: number; lng: number } | null = null;
  @Input() radiusKm: number | null = null;
  @Output() stationSelected = new EventEmitter<string>();
  @Output() userLocated = new EventEmitter<{ lat: number; lng: number }>();

  private readonly config = inject(STATION_FINDER_CONFIG);
  private map?: L.Map;
  private resize?: ResizeObserver;
  private markers = new Map<string, L.Marker>();
  private userMarker?: L.Marker;
  private userRadiusCircle?: L.Circle;
  private previousSelection: string | null = null;
  private hasInitiallyFitted = false;
  locating = false;

  ngAfterViewInit() {
    this.map = L.map(this.canvas.nativeElement, {
      zoomControl: false,
      attributionControl: false,
      minZoom: 5,
      maxZoom: 19,
    }).setView([16.05, 107.5], 6);

    L.tileLayer(this.config.tileUrl, {
      subdomains: this.config.subdomains || ['0', '1', '2', '3'],
      maxZoom: 20,
    }).addTo(this.map);

    if (typeof ResizeObserver !== 'undefined') {
      this.resize = new ResizeObserver(() => this.map?.invalidateSize());
      this.resize.observe(this.canvas.nativeElement);
    }

    this.render();
  }

  ngOnChanges() {
    this.render();
  }

  zoomIn() {
    this.map?.zoomIn();
  }

  zoomOut() {
    this.map?.zoomOut();
  }

  locateMe() {
    if (!navigator.geolocation || !this.map) return;
    this.locating = true;

    navigator.geolocation.getCurrentPosition(
      (pos) => {
        this.locating = false;
        const coords = { lat: pos.coords.latitude, lng: pos.coords.longitude };
        this.userLocated.emit(coords);
        this.updateUserPositionOnMap(coords.lat, coords.lng);

        // Khóa và giữ độ phóng to cận cảnh tối ưu (zoom >= 15), không zoom out
        const currentZoom = this.map?.getZoom() ?? 15;
        this.map?.flyTo([coords.lat, coords.lng], Math.max(currentZoom, 15), {
          animate: true,
          duration: 0.8,
        });
      },
      () => {
        this.locating = false;
      },
      { enableHighAccuracy: true, timeout: 8000 },
    );
  }

  updateUserPositionOnMap(lat: number, lng: number) {
    if (!this.map) return;
    const latLng: L.LatLngTuple = [lat, lng];

    if (!this.userMarker) {
      const userIcon = L.divIcon({
        className: 'user-gps-marker',
        html: `<div class="user-pulse"></div><div class="user-dot"></div>`,
        iconSize: [24, 24],
        iconAnchor: [12, 12],
      });
      this.userMarker = L.marker(latLng, { icon: userIcon, zIndexOffset: 2000 }).addTo(this.map);
    } else {
      this.userMarker.setLatLng(latLng);
    }

    // Vẽ hoặc cập nhật vòng tròn bán kính nếu có radiusKm
    if (this.radiusKm && this.radiusKm > 0) {
      const radiusMeters = this.radiusKm * 1000;
      if (!this.userRadiusCircle) {
        this.userRadiusCircle = L.circle(latLng, {
          radius: radiusMeters,
          color: '#2563eb',
          fillColor: '#3b82f6',
          fillOpacity: 0.08,
          weight: 1.5,
          dashArray: '4, 6',
        }).addTo(this.map);
      } else {
        this.userRadiusCircle.setLatLng(latLng).setRadius(radiusMeters);
      }
    } else if (this.userRadiusCircle) {
      this.userRadiusCircle.remove();
      this.userRadiusCircle = undefined;
    }
  }

  fitAll() {
    if (!this.map || !this.stations.length) return;
    this.map.fitBounds(
      this.stations.map((s) => [s.latitude, s.longitude] as L.LatLngTuple),
      { padding: [60, 60], maxZoom: 14 },
    );
  }

  private render() {
    if (!this.map) return;

    // Cập nhật vị trí GPS nếu được truyền từ bên ngoài
    if (this.userLocation) {
      this.updateUserPositionOnMap(this.userLocation.lat, this.userLocation.lng);

      if (!this.hasInitiallyFitted) {
        this.map.setView([this.userLocation.lat, this.userLocation.lng], 14);
        this.hasInitiallyFitted = true;
      }
    }

    const ids = new Set(this.stations.map((s) => s.id));

    // Dọn dẹp markers không còn trong danh sách
    for (const [id, marker] of this.markers) {
      if (!ids.has(id)) {
        marker.remove();
        this.markers.delete(id);
      }
    }

    // Vẽ markers theo chuẩn Google Maps Pin
    for (const station of this.stations) {
      const label = serviceLabel(station);
      const isSelected = station.id === this.selectedId;
      const symbol = station.offersCarCharging ? (station.offersBatterySwap ? '⚡↔' : '⚡') : '↔';
      const kind = station.offersCarCharging
        ? station.offersBatterySwap
          ? 'both'
          : 'charging'
        : 'swap';

      const pinColor =
        kind === 'charging' ? '#059669' : kind === 'swap' ? '#0284c7' : '#7c3aed';
      const pinShadow =
        kind === 'charging'
          ? 'rgba(5, 150, 105, 0.45)'
          : kind === 'swap'
            ? 'rgba(2, 132, 199, 0.45)'
            : 'rgba(124, 58, 237, 0.45)';

      const html = `
        <div class="gm-pin-wrapper ${kind} ${isSelected ? 'is-selected' : ''}" style="--pin-color: ${pinColor}; --pin-shadow: ${pinShadow}">
          <div class="gm-pin-pulse"></div>
          <div class="gm-pin-body">
            <span class="gm-pin-symbol" aria-hidden="true">${symbol}</span>
          </div>
          <div class="gm-pin-shadow-dot"></div>
        </div>
      `;

      const icon = L.divIcon({
        className: `station-marker ${kind}${isSelected ? ' is-selected' : ''}`,
        html,
        iconSize: [44, 52],
        iconAnchor: [22, 48],
      });

      let marker = this.markers.get(station.id);
      if (!marker) {
        marker = L.marker([station.latitude, station.longitude], {
          icon,
          keyboard: true,
          title: `${station.name} — ${label}`,
          alt: station.name,
        })
          .on('click', () => this.stationSelected.emit(station.id))
          .on('keydown', (event: L.LeafletKeyboardEvent) => {
            if (event.originalEvent.key === 'Enter' || event.originalEvent.key === ' ') {
              L.DomEvent.preventDefault(event.originalEvent);
              this.stationSelected.emit(station.id);
            }
          })
          .addTo(this.map);
        this.markers.set(station.id, marker);
      } else {
        marker.setLatLng([station.latitude, station.longitude]).setIcon(icon);
      }

      marker.setZIndexOffset(isSelected ? 1000 : 0);
      marker.getElement()?.setAttribute('aria-label', `${station.name} — ${label}`);
      marker.getElement()?.setAttribute('aria-pressed', String(isSelected));
    }

    if (!this.hasInitiallyFitted && this.stations.length && !this.userLocation) {
      this.fitAll();
      this.hasInitiallyFitted = true;
    }

    // Khi chọn một trạm mới: bay mượt đến trạm đó ở độ zoom tối ưu (giữ nguyên nếu người dùng đang zoom sâu hơn)
    if (this.selectedId && this.previousSelection !== this.selectedId) {
      const marker = this.markers.get(this.selectedId);
      if (marker) {
        const currentZoom = this.map.getZoom();
        const targetZoom = Math.max(currentZoom, 15);
        this.map.flyTo(marker.getLatLng(), targetZoom, { animate: true, duration: 0.6 });
      }
    }
    // Khi bỏ chọn (đóng chi tiết): giữ nguyên hoàn toàn góc nhìn và độ zoom của bản đồ
    this.previousSelection = this.selectedId;
  }

  ngOnDestroy() {
    this.resize?.disconnect();
    this.map?.remove();
  }
}
