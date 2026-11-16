import { Component, input, output } from '@angular/core';
import {
  directionsUrl,
  formatDistance,
  operatingStatusLabel,
  serviceLabel,
  Station,
} from '../models/station.model';

@Component({
  selector: 'app-station-list',
  template: `
    <ul class="stations" aria-label="Danh sách trạm">
      @for (station of stations(); track station.id) {
        <li class="station-card" [class.selected]="selectedId() === station.id" [class.is-inactive]="station.operatingStatus !== 'Open'">
          <button
            type="button"
            class="choose"
            [attr.aria-pressed]="selectedId() === station.id"
            (click)="stationSelected.emit(station.id)"
          >
            <!-- Service Icon -->
            <div
              class="card-icon"
              [class.inactive]="station.operatingStatus !== 'Open'"
              [title]="serviceLabel(station)"
            >
              @if (station.offersCarCharging && station.offersBatterySwap) {
                <div class="dual-icon-badge" aria-hidden="true">
                  <svg viewBox="0 0 24 24" class="icon-bolt" width="12" height="12" fill="currentColor">
                    <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z" />
                  </svg>
                  <svg viewBox="0 0 24 24" class="icon-battery" width="12" height="12" fill="currentColor">
                    <path d="M16 4h-1V2.5a.5.5 0 0 0-.5-.5h-5a.5.5 0 0 0-.5.5V4H8C6.34 4 5 5.34 5 7v12c0 1.66 1.34 3 3 3h8c1.66 0 3-1.34 3-3V7c0-1.66-1.34-3-3-3zm-1 12H9v-2h6v2zm0-4H9v-2h6v2zm0-4H9V6h6v2z" />
                  </svg>
                </div>
              } @else if (station.offersCarCharging) {
                <svg viewBox="0 0 24 24" class="icon-bolt" width="18" height="18" fill="currentColor" aria-hidden="true">
                  <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z" />
                </svg>
              } @else {
                <svg viewBox="0 0 24 24" class="icon-battery" width="18" height="18" fill="currentColor" aria-hidden="true">
                  <path d="M16 4h-1V2.5a.5.5 0 0 0-.5-.5h-5a.5.5 0 0 0-.5.5V4H8C6.34 4 5 5.34 5 7v12c0 1.66 1.34 3 3 3h8c1.66 0 3-1.34 3-3V7c0-1.66-1.34-3-3-3zm-1 12H9v-2h6v2zm0-4H9v-2h6v2zm0-4H9V6h6v2z" />
                </svg>
              }
            </div>

            <!-- Card Main Content -->
            <div class="card-content">
              <!-- Row 1: Station Name (up to 2 lines allowed) + Status (only for Open) -->
              <div class="card-title-row">
                <strong class="card-name" [title]="station.name">{{ station.name }}</strong>
                @if (station.operatingStatus === 'Open') {
                  <span class="status-tag open">
                    <span class="status-dot"></span>
                    {{ operatingStatusLabel(station) }}
                  </span>
                }
              </div>

              <!-- Row 2: Distance & Address (Regular text, NO pastel yellow pill) -->
              <div class="card-location">
                @if (station.distanceKm != null) {
                  <span class="distance-text">{{ formatDistance(station.distanceKm) }}</span>
                  <span class="location-dot">•</span>
                }
                <span class="address" [title]="station.address">{{ station.address }}</span>
              </div>

              <!-- Row 3: HERO CAPACITY METRIC or SINGLE INACTIVE WARNING -->
              <div class="card-capacity-row">
                @if (station.operatingStatus === 'Open') {
                  <div class="capacity-stats">
                    @if (station.offersCarCharging) {
                      <span class="stat-unit">
                        <svg viewBox="0 0 24 24" width="12" height="12" fill="currentColor">
                          <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z" />
                        </svg>
                        <span>Sạc: <strong>{{ station.availableChargingBays ?? 0 }}/10</strong> bãi</span>
                      </span>
                    }
                    @if (station.offersCarCharging && station.offersBatterySwap) {
                      <span class="stat-sep">•</span>
                    }
                    @if (station.offersBatterySwap) {
                      <span class="stat-unit">
                        <svg viewBox="0 0 24 24" width="12" height="12" fill="currentColor">
                          <path d="M16 4h-1V2.5a.5.5 0 0 0-.5-.5h-5a.5.5 0 0 0-.5.5V4H8C6.34 4 5 5.34 5 7v12c0 1.66 1.34 3 3 3h8c1.66 0 3-1.34 3-3V7c0-1.66-1.34-3-3-3zm-1 12H9v-2h6v2zm0-4H9v-2h6v2zm0-4H9V6h6v2z" />
                        </svg>
                        <span>Pin: <strong>{{ station.availableBatteries ?? 0 }}/20</strong> bình</span>
                      </span>
                    }
                  </div>
                } @else if (station.operatingStatus === 'Maintenance') {
                  <span class="status-tag inactive-line maintenance">
                    <span class="status-dot"></span>
                    Đang bảo trì
                  </span>
                } @else {
                  <span class="status-tag inactive-line closed">
                    <span class="status-dot"></span>
                    Đóng cửa
                  </span>
                }
              </div>
            </div>

            <!-- Direction link: Subtle icon button on the right -->
            <a
              class="directions directions-link"
              [href]="directionsUrl(station)"
              target="_blank"
              rel="noopener noreferrer"
              (click)="$event.stopPropagation()"
              [attr.aria-label]="'Chỉ đường đến ' + station.name + ' (mở tab mới)'"
              title="Chỉ đường trên Google Maps"
            >
              <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                <line x1="7" y1="17" x2="17" y2="7"></line>
                <polyline points="7 7 17 7 17 17"></polyline>
              </svg>
            </a>
          </button>
        </li>
      }
    </ul>
  `,
  styles: `
    .stations {
      list-style: none;
      padding: 0;
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .station-card {
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      background: #ffffff;
      padding: 11px 12px;
      transition: all 0.18s ease;
      position: relative;
    }
    .station-card.is-inactive {
      opacity: 0.52;
      background: #fbfcfd;
    }
    .station-card.is-inactive:hover {
      opacity: 0.85;
    }
    .station-card:hover {
      border-color: #94a3b8;
      box-shadow: 0 3px 10px rgba(0, 0, 0, 0.05);
    }
    .station-card:hover .directions-link {
      opacity: 1;
      color: #17634f;
      background: #eaf5ed;
    }
    .selected {
      border-color: #17634f !important;
      background: #f4f8f6 !important;
      box-shadow: 0 0 0 1px #17634f !important;
    }
    .choose {
      display: flex;
      align-items: flex-start;
      gap: 11px;
      width: 100%;
      padding: 0;
      text-align: left;
      border: 0;
      background: none;
      color: #173e34;
      cursor: pointer;
      font: inherit;
    }
    .card-icon {
      flex: none;
      width: 36px;
      height: 36px;
      border-radius: 8px;
      display: grid;
      place-items: center;
      background: #eaf5ed;
      color: #17634f;
      border: 1px solid rgba(23, 99, 79, 0.15);
      margin-top: 1px;
    }
    .card-icon.inactive {
      background: #f1f5f9 !important;
      color: #94a3b8 !important;
      border-color: #e2e8f0 !important;
    }
    .dual-icon-badge {
      display: flex;
      align-items: center;
      gap: 2px;
    }
    .card-content {
      flex: 1;
      min-width: 0;
    }
    .card-title-row {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 6px;
      margin-bottom: 3px;
    }
    .card-name {
      font-size: 13.5px;
      font-weight: 700;
      color: #173e34;
      line-height: 1.35;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
      text-overflow: ellipsis;
      word-break: break-word;
      flex: 1;
    }
    .status-tag {
      font-size: 11px;
      font-weight: 600;
      color: #64748b;
      white-space: nowrap;
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 1px 6px;
      border-radius: 4px;
      background: #f1f5f9;
      flex: none;
      margin-top: 1px;
    }
    .status-dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: currentColor;
    }
    .status-tag.open {
      color: #17634f;
      background: #eaf5ed;
    }
    .status-tag.closed {
      color: #64748b;
      background: #f1f5f9;
    }
    .status-tag.maintenance {
      color: #b91c1c;
      background: #fee2e2;
    }
    .card-location {
      display: flex;
      align-items: center;
      gap: 5px;
      font-size: 12px;
      color: #64748b;
      margin-bottom: 6px;
      line-height: 1.3;
    }
    .distance-text {
      font-weight: 600;
      color: #173e34;
      white-space: nowrap;
    }
    .location-dot {
      color: #cbd5e1;
    }
    .address {
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      flex: 1;
      min-width: 0;
    }
    .card-capacity-row {
      display: flex;
      align-items: center;
      font-size: 12px;
    }
    .capacity-stats {
      display: flex;
      align-items: center;
      gap: 6px;
      color: #17634f;
      font-weight: 500;
    }
    .stat-unit {
      display: inline-flex;
      align-items: center;
      gap: 4px;
    }
    .stat-unit strong {
      font-weight: 700;
      color: #104f3e;
    }
    .stat-sep {
      color: #cbd5e1;
    }
    .inactive-line {
      display: inline-flex;
      align-items: center;
      gap: 5px;
      font-size: 11.5px;
      font-weight: 600;
      padding: 0;
      background: transparent !important;
      border: none !important;
    }
    .inactive-line.maintenance {
      color: #b91c1c;
    }
    .inactive-line.closed {
      color: #b91c1c;
    }
    .inactive-line .status-dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: currentColor;
    }
    .directions-link {
      flex: none;
      width: 28px;
      height: 28px;
      border-radius: 6px;
      display: grid;
      place-items: center;
      color: #94a3b8;
      background: transparent;
      text-decoration: none;
      transition: all 0.15s ease;
      opacity: 0.4;
      align-self: center;
    }
    .directions-link:hover {
      opacity: 1 !important;
      background: #eaf5ed !important;
      color: #17634f !important;
    }
  `,
})
export class StationListComponent {
  readonly stations = input.required<Station[]>();
  readonly selectedId = input<string | null>(null);
  readonly stationSelected = output<string>();
  readonly directionsUrl = directionsUrl;
  readonly serviceLabel = serviceLabel;
  readonly formatDistance = formatDistance;
  readonly operatingStatusLabel = operatingStatusLabel;
}
