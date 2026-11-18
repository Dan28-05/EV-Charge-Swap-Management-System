import { Injectable, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';

export interface StationInventoryUpdatedEvent {
  locationId: string;
  availableChargingBays: number | null;
  availableBatteries: number | null;
  updatedAtUtc: string;
}

export type RealtimeConnectionStatus = 'connected' | 'reconnecting' | 'disconnected';

@Injectable({ providedIn: 'root' })
export class StationRealtimeService {
  private hubConnection: HubConnection | null = null;
  private readonly inventorySubject = new Subject<StationInventoryUpdatedEvent>();
  private activeStationId: string | null = null;
  private connectingPromise: Promise<void> | null = null;

  readonly connectionStatus = signal<RealtimeConnectionStatus>('disconnected');
  readonly stationUpdated$: Observable<StationInventoryUpdatedEvent> =
    this.inventorySubject.asObservable();

  constructor() {
    this.initHubConnection();
  }

  private initHubConnection(): void {
    if (typeof window === 'undefined') return;

    let hubUrl = 'http://localhost:8080/hubs/stations';
    try {
      if (
        window.location &&
        window.location.origin &&
        window.location.origin !== 'null' &&
        !window.location.href.startsWith('about:')
      ) {
        hubUrl = `${window.location.origin}/hubs/stations`;
      }
    } catch {
      hubUrl = 'http://localhost:8080/hubs/stations';
    }

    try {
      this.hubConnection = new HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(LogLevel.Warning)
        .build();

      this.hubConnection.on(
        'StationInventoryUpdated',
        (payload: StationInventoryUpdatedEvent) => {
          this.inventorySubject.next(payload);
        },
      );

      this.hubConnection.onreconnecting(() => {
        this.connectionStatus.set('reconnecting');
      });

      this.hubConnection.onreconnected(() => {
        this.connectionStatus.set('connected');
        if (this.activeStationId) {
          this.hubConnection
            ?.invoke('WatchStation', this.activeStationId)
            .catch(() => {});
        }
      });

      this.hubConnection.onclose(() => {
        this.connectionStatus.set('disconnected');
      });
    } catch {
      // In non-browser or test runner environments where WebSocket/SignalR cannot start
      this.connectionStatus.set('disconnected');
    }
  }

  async ensureConnected(): Promise<void> {
    if (!this.hubConnection) return;
    if (this.hubConnection.state === HubConnectionState.Connected) {
      this.connectionStatus.set('connected');
      return;
    }
    if (this.connectingPromise) return this.connectingPromise;

    this.connectingPromise = (async () => {
      try {
        if (this.hubConnection?.state === HubConnectionState.Disconnected) {
          await this.hubConnection.start();
          this.connectionStatus.set('connected');
        }
      } catch (err) {
        this.connectionStatus.set('disconnected');
      } finally {
        this.connectingPromise = null;
      }
    })();

    return this.connectingPromise;
  }

  async watchStation(stationId: string): Promise<void> {
    this.activeStationId = stationId;
    await this.ensureConnected();

    if (this.hubConnection?.state === HubConnectionState.Connected) {
      try {
        await this.hubConnection.invoke('WatchStation', stationId);
      } catch {
        // Fallback or retry will be handled automatically on reconnect
      }
    }
  }

  async unwatchStation(stationId: string): Promise<void> {
    if (this.activeStationId === stationId) {
      this.activeStationId = null;
    }

    if (this.hubConnection?.state === HubConnectionState.Connected) {
      try {
        await this.hubConnection.invoke('UnwatchStation', stationId);
      } catch {
        // Ignore unwatch errors on teardown
      }
    }
  }
}
