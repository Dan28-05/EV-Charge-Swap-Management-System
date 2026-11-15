import { Component, input, output } from '@angular/core';
import { directionsUrl, formatDistance, serviceLabel, Station } from '../models/station.model';

@Component({
  selector: 'app-station-list',
  template: `
    <ul class="stations" aria-label="Danh sách trạm">
      @for (station of stations(); track station.id) {
        <li class="station-card" [class.selected]="selectedId() === station.id">
          <button
            type="button"
            class="choose"
            [attr.aria-pressed]="selectedId() === station.id"
            (click)="stationSelected.emit(station.id)"
          >
            <div class="card-icon" [class.charging]="station.offersCarCharging" [class.swap]="station.offersBatterySwap">
              <span aria-hidden="true">{{
                station.offersCarCharging ? (station.offersBatterySwap ? '⚡↔' : '⚡') : '↔'
              }}</span>
            </div>

            <div class="card-content">
              <div class="card-title-row">
                <strong class="card-name">{{ station.name }}</strong>
                <span class="status-tag">● Mở cửa</span>
              </div>

              <span class="address">{{ station.address }}</span>

              <div class="service-badges">
                @if (station.distanceKm != null) {
                  <span class="badge distance-badge">📍 Cách {{ formatDistance(station.distanceKm) }}</span>
                }
                @if (station.offersCarCharging) {
                  <span class="badge charge-badge">⚡ Sạc nhanh DC</span>
                }
                @if (station.offersBatterySwap) {
                  <span class="badge swap-badge">↔ Đổi pin 3p</span>
                }
              </div>
            </div>
          </button>

          <div class="card-actions">
            <a
              class="directions-link"
              [href]="directionsUrl(station)"
              target="_blank"
              rel="noopener noreferrer"
              [attr.aria-label]="'Chỉ đường đến ' + station.name + ' (mở tab mới)'"
            >
              <svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor">
                <path d="M12 2L4.5 20.29l.71.71L12 18l6.79 3 .71-.71z" />
              </svg>
              <span>Chỉ đường</span>
            </a>
          </div>
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
      gap: 12px;
    }
    .station-card {
      border: 1px solid #e5e7eb;
      border-radius: 14px;
      background: #ffffff;
      padding: 14px 16px;
      transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
      position: relative;
    }
    .station-card:hover {
      border-color: #cbd5e1;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
      transform: translateY(-1px);
    }
    .selected {
      border-color: #10b981 !important;
      background: #f0fdf4 !important;
      box-shadow: 0 0 0 2px rgba(16, 185, 129, 0.25), 0 4px 14px rgba(0, 0, 0, 0.08) !important;
    }
    .choose {
      display: flex;
      gap: 14px;
      width: 100%;
      padding: 0;
      text-align: left;
      border: 0;
      background: none;
      color: #1f2937;
      cursor: pointer;
      font: inherit;
    }
    .card-icon {
      flex: none;
      width: 40px;
      height: 40px;
      border-radius: 12px;
      display: grid;
      place-items: center;
      background: #ecfdf5;
      color: #059669;
      font-size: 18px;
      font-weight: 800;
      border: 1px solid rgba(5, 150, 105, 0.2);
    }
    .card-icon.swap {
      background: #eff6ff;
      color: #2563eb;
      border-color: rgba(37, 99, 235, 0.2);
    }
    .card-content {
      flex: 1;
      min-width: 0;
    }
    .card-title-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
    }
    .card-name {
      font-size: 15px;
      font-weight: 700;
      color: #111827;
      line-height: 1.35;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .status-tag {
      font-size: 11px;
      font-weight: 600;
      color: #059669;
      white-space: nowrap;
    }
    .address {
      display: block;
      color: #6b7280;
      font-size: 13px;
      margin: 4px 0 8px;
      line-height: 1.45;
    }
    .service-badges {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }
    .badge {
      font-size: 11px;
      font-weight: 600;
      padding: 3px 8px;
      border-radius: 6px;
    }
    .distance-badge {
      background: #fef3c7;
      color: #92400e;
      border: 1px solid rgba(245, 158, 11, 0.25);
    }
    .charge-badge {
      background: #d1fae5;
      color: #065f46;
    }
    .swap-badge {
      background: #dbeafe;
      color: #1e40af;
    }
    .card-actions {
      display: flex;
      justify-content: flex-end;
      margin-top: 8px;
      padding-top: 8px;
      border-top: 1px dashed #f3f4f6;
    }
    .directions-link {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      font-weight: 700;
      color: #2563eb;
      text-decoration: none;
      padding: 4px 10px;
      border-radius: 6px;
      background: #eff6ff;
      transition: all 0.15s ease;
    }
    .directions-link:hover {
      background: #dbeafe;
      color: #1d4ed8;
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
}
