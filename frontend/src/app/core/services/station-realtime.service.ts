import { DestroyRef, Injectable, inject, signal } from '@angular/core';
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
  private readonly destroyRef = inject(DestroyRef);
  private hubConnection: HubConnection | null = null;
  private readonly inventorySubject = new Subject<StationInventoryUpdatedEvent>();
  private stationIds: string[] = [];
  private subscriptionQueue: Promise<void> = Promise.resolve();
  private reconnectTimer?: ReturnType<typeof setTimeout>;
  private connectingPromise: Promise<void> | null = null;
  private destroyed = false;

  readonly connectionStatus = signal<RealtimeConnectionStatus>('disconnected');
  readonly stationUpdated$: Observable<StationInventoryUpdatedEvent> =
    this.inventorySubject.asObservable();

  constructor() {
    this.initHubConnection();
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.clearReconnectTimer();
      void this.hubConnection?.stop().catch(() => undefined);
      this.inventorySubject.complete();
    });
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
        this.syncSubscriptions();
      });

      this.hubConnection.onclose(() => {
        this.connectionStatus.set('disconnected');
        this.scheduleRetry();
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
      } catch {
        this.connectionStatus.set('disconnected');
        this.scheduleRetry();
      } finally {
        this.connectingPromise = null;
      }
    })();

    return this.connectingPromise;
  }

  watchStations(stationIds: string[]): void {
    const ids = [...new Set(stationIds)].slice(0, 100).sort();
    if (ids.join(',') === this.stationIds.join(',')) return;
    this.stationIds = ids;
    this.syncSubscriptions();
  }

  private syncSubscriptions(): void {
    this.subscriptionQueue = this.subscriptionQueue.then(async () => {
      if (this.destroyed) return;
      if (this.stationIds.length === 0) {
        this.clearReconnectTimer();
        if (this.hubConnection?.state === HubConnectionState.Connected) {
          await this.hubConnection.invoke('WatchStations', []);
          await this.hubConnection.stop();
        }
        this.connectionStatus.set('disconnected');
        return;
      }

      await this.ensureConnected();
      if (this.destroyed || this.hubConnection?.state !== HubConnectionState.Connected) return;
      const snapshot = await this.hubConnection.invoke<StationInventoryUpdatedEvent[]>('WatchStations', this.stationIds);
      snapshot.forEach((item) => this.inventorySubject.next(item));
      this.connectionStatus.set('connected');
    }).catch(() => {
      this.connectionStatus.set('disconnected');
      this.scheduleRetry();
    });
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer !== undefined) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = undefined;
    }
  }

  private scheduleRetry(): void {
    if (this.destroyed || this.stationIds.length === 0 || this.reconnectTimer !== undefined) return;
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = undefined;
      this.syncSubscriptions();
    }, 8000);
  }
}
