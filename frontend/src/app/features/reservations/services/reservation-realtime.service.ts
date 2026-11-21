import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, effect, inject, signal } from '@angular/core';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Subject, firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth/services/auth.service';

export interface RealtimeNotification {
  id: string;
  title: string;
  message: string;
  isRead: boolean;
  createdAtUtc: string;
  actionUrl: string | null;
  type?: string;
}

@Injectable({ providedIn: 'root' })
export class ReservationRealtimeService {
  private readonly auth = inject(AuthService, { optional: true });
  private readonly changed = new Subject<string | null>();
  readonly changed$ = this.changed.asObservable();
  private readonly operatorChanged = new Subject<void>();
  readonly operatorChanged$ = this.operatorChanged.asObservable();
  private readonly invoiceChanged = new Subject<void>();
  readonly invoiceChanged$ = this.invoiceChanged.asObservable();
  private readonly notificationsChanged = new Subject<void>();
  readonly notificationsChanged$ = this.notificationsChanged.asObservable();
  private readonly receivedNotification = new Subject<RealtimeNotification>();
  readonly receivedNotification$ = this.receivedNotification.asObservable();
  readonly connected = signal(false);

  constructor() {
    if (!this.auth || !isPlatformBrowser(inject(PLATFORM_ID))) return;
    effect((onCleanup) => {
      const user = this.auth?.currentUser();
      if (!user || !user.roles.some((role) => role === 'Customer' || role === 'Operator')) return;

      let disposed = false;
      let retry: ReturnType<typeof setTimeout> | undefined;
      const hub = new HubConnectionBuilder()
        .withUrl('/hubs/notifications', {
          accessTokenFactory: async () => {
            const auth = this.auth;
            const session = auth?.session();
            if (!auth || !session || session.user.id !== user.id) return '';
            if (Date.parse(session.accessTokenExpiresAtUtc) > Date.now()) return session.accessToken;
            const renewed = await firstValueFrom(auth.refreshToken());
            return !disposed && auth.currentUser()?.id === user.id ? renewed.accessToken : '';
          },
        })
        .withAutomaticReconnect([0, 2000, 5000, 15000])
        .configureLogging(LogLevel.None)
        .build();

      const connected = () => {
        if (disposed) return;
        this.connected.set(true);
        this.invoiceChanged.next();
        this.notificationsChanged.next();
        if (user.roles.includes('Customer')) this.changed.next(null);
        if (user.roles.includes('Operator')) this.operatorChanged.next();
      };
      const start = () => {
        if (!disposed) void hub.start().then(connected, scheduleRetry);
      };
      const scheduleRetry = () => {
        if (!disposed) {
          this.connected.set(false);
          retry = setTimeout(start, 8000);
        }
      };
      hub.on('InvoiceChanged', () => {
        if (!disposed) this.invoiceChanged.next();
      });
      hub.on('ReceiveNotification', (notification: RealtimeNotification | undefined) => {
        if (disposed) return;
        this.notificationsChanged.next();
        if (notification?.id) this.receivedNotification.next(notification);
      });
      hub.on('ReservationChanged', (id: string) => {
        if (!disposed && typeof id === 'string') this.changed.next(id);
      });
      hub.on('OperatorStationChanged', () => {
        if (!disposed && user.roles.includes('Operator')) this.operatorChanged.next();
      });
      hub.onreconnecting(() => {
        if (!disposed) this.connected.set(false);
      });
      hub.onreconnected(connected);
      hub.onclose(scheduleRetry);
      start();

      onCleanup(() => {
        disposed = true;
        clearTimeout(retry);
        this.connected.set(false);
        void hub.stop().catch(() => undefined);
      });
    });
  }
}
