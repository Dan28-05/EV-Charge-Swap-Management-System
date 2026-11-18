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
    <div #canvas class="canvas" aria-label="Bản đồ trạm sạc và đổi pin OpenStreetMap"></div>

    <div class="map-floating-controls" [class.panel-open]="!!selectedId" aria-label="Điều khiển bản đồ">
      <div class="control-group zoom-group">
        <button
          type="button"
          class="ctrl-btn"
          aria-label="Phóng to bản đồ"
          title="Phóng to"
          (click)="zoomIn()"
        >
          <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2.5">
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
          <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2.5">
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
        <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2">
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
        <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2">
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
      right: 14px;
      bottom: 14px;
      z-index: 1000;
      display: flex;
      flex-direction: row;
      gap: 6px;
      align-items: center;
    }
    .control-group {
      background: #ffffff;
      border-radius: 8px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12);
      overflow: hidden;
      display: flex;
      flex-direction: row;
      align-items: center;
      border: 1px solid rgba(0, 0, 0, 0.08);
    }
    .single-btn {
      background: #ffffff;
      border-radius: 8px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12);
      border: 1px solid rgba(0, 0, 0, 0.08);
    }
    .ctrl-btn {
      width: 32px;
      height: 32px;
      display: grid;
      place-items: center;
      background: #ffffff;
      border: none;
      color: #374151;
      cursor: pointer;
      padding: 0;
      transition: all 0.15s ease;
    }
    .ctrl-btn:hover {
      background: #f1f5f9;
      color: #0f172a;
    }
    .ctrl-btn:active {
      background: #e2e8f0;
      transform: scale(0.96);
    }
    .ctrl-btn.active {
      color: #17634f;
      background: #eaf5ed;
    }
    .divider {
      width: 1px;
      height: 16px;
      background: #e2e8f0;
    }
    @media (max-width: 768px) {
      .map-floating-controls {
        right: 12px;
        bottom: 14px;
        gap: 5px;
        transition: bottom 0.25s cubic-bezier(0.4, 0, 0.2, 1);
      }
      .map-floating-controls.panel-open {
        bottom: calc(55vh + 12px);
      }
      .ctrl-btn {
        width: 30px;
        height: 30px;
      }
      .divider {
        height: 14px;
      }
    }
  `,
})
export class StationMapComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('canvas', { static: true }) canvas!: ElementRef<HTMLDivElement>;
  @Input() stations: Station[] = [];
  @Input() selectedId: string | null = null;
  @Input() sidebarCollapsed = false;
  @Input() userLocation: { lat: number; lng: number } | null = null;
  @Input() searchCenter: { lat: number; lng: number } | null = null;
  @Input() radiusKm: number | null = null;
  @Output() stationSelected = new EventEmitter<string>();
  @Output() userLocated = new EventEmitter<{ lat: number; lng: number }>();
  @Output() mapAreaChanged = new EventEmitter<{ lat: number; lng: number }>();

  private readonly config = inject(STATION_FINDER_CONFIG);
  private map?: L.Map;
  private resize?: ResizeObserver;
  private markers = new Map<string, L.Marker>();
  private userMarker?: L.Marker;
  private userRadiusCircle?: L.Circle;
  private previousSelection: string | null = null;
  private previousSearchCenter: string | null = null;
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

    this.map.on('dragend', () => {
      const center = this.map?.getCenter();
      if (center) this.mapAreaChanged.emit({ lat: center.lat, lng: center.lng });
    });

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

        this.map?.setView([coords.lat, coords.lng], this.map.getZoom(), { animate: false });
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
    const isMobile = (this.map.getSize().x || window.innerWidth) <= 768;
    const leftPad = isMobile ? 20 : (!this.sidebarCollapsed ? 420 : 30);
    const rightPad = isMobile ? 20 : (this.selectedId ? 415 : 30);
    this.map.fitBounds(
      this.stations.map((s) => [s.latitude, s.longitude] as L.LatLngTuple),
      {
        paddingTopLeft: [leftPad, 50],
        paddingBottomRight: [rightPad, 50],
        maxZoom: 14,
      },
    );
  }

  private render() {
    if (!this.map) return;

    // Cập nhật vị trí GPS nếu được truyền từ bên ngoài
    if (this.userLocation) {
      this.updateUserPositionOnMap(this.userLocation.lat, this.userLocation.lng);

      if (!this.hasInitiallyFitted) {
        this.map.setView([this.userLocation.lat, this.userLocation.lng], this.map.getZoom());
        this.hasInitiallyFitted = true;
      }
    }

    // The search radius follows the area the customer is browsing, not necessarily their GPS dot.
    if (this.searchCenter) {
      const center: L.LatLngTuple = [this.searchCenter.lat, this.searchCenter.lng];
      const centerKey = `${this.searchCenter.lat.toFixed(5)},${this.searchCenter.lng.toFixed(5)}`;
      if (this.previousSearchCenter !== centerKey) {
        this.map.setView(center, this.map.getZoom(), { animate: false });
        this.hasInitiallyFitted = true;
      }
      this.previousSearchCenter = centerKey;
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
      const boltSvg = `<svg class="pin-svg pin-bolt" viewBox="0 0 24 24" width="15" height="15" fill="currentColor"><path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/></svg>`;
      const batterySvg = `<svg class="pin-svg pin-battery" viewBox="0 0 24 24" width="14" height="14" fill="currentColor"><path d="M16 4h-1V2.5a.5.5 0 0 0-.5-.5h-5a.5.5 0 0 0-.5.5V4H8C6.34 4 5 5.34 5 7v12c0 1.66 1.34 3 3 3h8c1.66 0 3-1.34 3-3V7c0-1.66-1.34-3-3-3zm-1 12H9v-2h6v2zm0-4H9v-2h6v2zm0-4H9V6h6v2z"/></svg>`;
      const dualSvg = `<div class="pin-svg-dual">${boltSvg}${batterySvg}</div>`;

      const symbol = station.offersCarCharging
        ? (station.offersBatterySwap ? dualSvg : boltSvg)
        : batterySvg;
      const kind = station.offersCarCharging
        ? station.offersBatterySwap
          ? 'both'
          : 'charging'
        : 'swap';

      const isInactive = station.operatingStatus === 'Maintenance' || station.operatingStatus === 'Closed';
      const pinColor = isInactive ? '#94a3b8' : '#17634f';
      const pinShadow = isInactive
        ? 'rgba(148, 163, 184, 0.35)'
        : 'rgba(23, 99, 79, 0.45)';

      const html = `
        <div class="gm-pin-wrapper ${kind} ${isInactive ? 'is-inactive' : ''} ${isSelected ? 'is-selected' : ''}" style="--pin-color: ${pinColor}; --pin-shadow: ${pinShadow}">
          ${isInactive ? '' : '<div class="gm-pin-pulse"></div>'}
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

    // Khi chọn một trạm mới: tính toán trọng tâm để marker hiển thị chính giữa vùng thông thoáng (không bị đè bởi Panel)
    if (this.selectedId && this.previousSelection !== this.selectedId) {
      const marker = this.markers.get(this.selectedId);
      if (marker) {
        const currentZoom = this.map.getZoom();
        const targetZoom = currentZoom;
        const width = this.map.getSize().x || 1400;
        const height = this.map.getSize().y || 900;
        const isMobile = width <= 768;

        if (isMobile) {
          this.map.setView(marker.getLatLng(), targetZoom, { animate: true });
          const point = this.map.latLngToContainerPoint(marker.getLatLng());
          this.map.panBy([0, point.y - height * 0.25], { animate: true, duration: 0.5 });
        } else {
          const leftPad = !this.sidebarCollapsed ? 420 : 30;
          const rightPad = 410; // Panel chi tiết chiếm 380px + lề
          const visibleCenterX = (leftPad + (width - rightPad)) / 2;
          const shiftX = (width / 2) - visibleCenterX;

          const targetPoint = this.map.project(marker.getLatLng(), targetZoom).add([shiftX, 0]);
          const targetLatLng = this.map.unproject(targetPoint, targetZoom);

          this.map.setView(targetLatLng, targetZoom, { animate: false });
        }
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
