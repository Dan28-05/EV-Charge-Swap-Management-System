import { isPlatformBrowser } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { DestroyRef, Injectable, PLATFORM_ID, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Observable, auditTime, catchError, finalize, map, of, switchMap, tap, throwError } from 'rxjs';
import { ReservationRealtimeService } from './reservation-realtime.service';
import { AuthService } from '../../auth/services/auth.service';
import {
  CheckInRequest,
  HoldRequest,
  ReservationResponse,
} from '../models/reservation.models';

@Injectable({ providedIn: 'root' })
export class ReservationService {
  private readonly http = inject(HttpClient);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly authService = inject(AuthService, { optional: true });
  private readonly realtime = inject(ReservationRealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  private expiryLookupPending = false;
  private refreshPending = false;
  private queuedChange: { id: string | null } | null = null;
  private stateVersion = 0;

  readonly activeReservation = signal<ReservationResponse | null>(null);
  readonly lastClosedReservation = signal<ReservationResponse | null>(null);
  readonly loading = signal<boolean>(false);
  readonly remainingSeconds = signal<number>(0);

  private countdownTimer: ReturnType<typeof setInterval> | null = null;
  private pollTimer: ReturnType<typeof setInterval> | null = null;

  readonly hasActiveReservation = computed<boolean>(() => !!this.activeReservation());
  readonly isHeld = computed<boolean>(() => this.activeReservation()?.status === 'Held');
  readonly isCheckedIn = computed<boolean>(() => this.activeReservation()?.status === 'CheckedIn');

  readonly formattedCountdown = computed<string>(() => {
    const s = this.remainingSeconds();
    if (s <= 0) return '00:00';
    const mins = Math.floor(s / 60);
    const secs = s % 60;
    return `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
  });

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.stopCountdownLoop();
      this.stopPolling();
    });
    this.realtime.changed$.pipe(auditTime(100), takeUntilDestroyed(this.destroyRef))
      .subscribe((id) => this.syncActive({ id }));

    if (this.authService) {
      effect(() => {
        if (!this.authService?.isAuthenticated()) {
          this.setActive(null);
          this.lastClosedReservation.set(null);
        }
      });
    }
  }

  hold(request: HoldRequest, idempotencyKey?: string): Observable<ReservationResponse> {
    this.loading.set(true);
    const key = idempotencyKey ?? this.newIdempotencyKey();
    const headers = new HttpHeaders({ 'Idempotency-Key': key });
    return this.http.post<ReservationResponse>('/api/reservations', request, { headers }).pipe(
      tap((res) => {
        this.loading.set(false);
        this.setActive(res);
      }),
      catchError((err: HttpErrorResponse) => {
        this.loading.set(false);
        return throwError(() => err);
      }),
    );
  }

  getActive(): Observable<ReservationResponse | null> {
    const version = this.stateVersion;
    const userId = this.authService?.currentUser()?.id;
    const isCurrent = () => this.stateVersion === version && this.authService?.currentUser()?.id === userId;
    return this.http.get<ReservationResponse>('/api/reservations/active').pipe(
      map((res) => {
        if (isCurrent()) this.setActive(res);
        return res;
      }),
      catchError((err: HttpErrorResponse) => {
        if (err.status === 404 || err.status === 401) {
          if (isCurrent()) this.setActive(null);
          return of(null);
        }
        return throwError(() => err);
      }),
    );
  }

  getById(id: string): Observable<ReservationResponse> {
    return this.http.get<ReservationResponse>(`/api/reservations/${id}`);
  }

  cancel(id: string): Observable<void> {
    this.loading.set(true);
    return this.http.post<void>(`/api/reservations/${id}/cancel`, {}).pipe(
      tap(() => {
        this.loading.set(false);
        const current = this.activeReservation();
        if (current && current.id === id) {
          this.setActive(null);
        }
      }),
      catchError((err: HttpErrorResponse) => {
        this.loading.set(false);
        return throwError(() => err);
      }),
    );
  }

  checkIn(request: CheckInRequest): Observable<ReservationResponse> {
    this.loading.set(true);
    return this.http.post<ReservationResponse>('/api/reservations/check-in', request).pipe(
      tap((res) => {
        this.loading.set(false);
        const current = this.activeReservation();
        if (current && current.id === res.id) {
          this.setActive(res);
        }
      }),
      catchError((err: HttpErrorResponse) => {
        this.loading.set(false);
        return throwError(() => err);
      }),
    );
  }

  closeWithoutService(id: string, locationId: string): Observable<void> {
    this.loading.set(true);
    return this.http
      .post<void>(`/api/reservations/${id}/close-without-service?locationId=${encodeURIComponent(locationId)}`, {})
      .pipe(
        tap(() => {
          this.loading.set(false);
          const current = this.activeReservation();
          if (current && current.id === id) {
            this.setActive(null);
          }
        }),
        catchError((err: HttpErrorResponse) => {
          this.loading.set(false);
          return throwError(() => err);
        }),
      );
  }

  setActive(reservation: ReservationResponse | null): void {
    this.stateVersion++;
    if (reservation) this.lastClosedReservation.set(null);
    this.activeReservation.set(reservation);
    this.updateCountdown();

    if (reservation?.status === 'Held') {
      this.startCountdownLoop();
    } else {
      this.stopCountdownLoop();
    }

    if (reservation && (reservation.status === 'Held' || reservation.status === 'CheckedIn')) {
      this.startPolling();
    } else {
      this.stopPolling();
    }
  }

  private startCountdownLoop(): void {
    if (!isPlatformBrowser(this.platformId) || this.countdownTimer) return;
    this.countdownTimer = setInterval(() => {
      this.updateCountdown();
    }, 1000);
  }

  private stopCountdownLoop(): void {
    if (this.countdownTimer) {
      clearInterval(this.countdownTimer);
      this.countdownTimer = null;
    }
  }

  private newIdempotencyKey(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = (Math.random() * 16) | 0;
      const v = c === 'x' ? r : (r & 0x3) | 0x8;
      return v.toString(16);
    });
  }

  private updateCountdown(): void {
    const res = this.activeReservation();
    if (!res || res.status !== 'Held' || !res.expiresAtUtc) {
      this.remainingSeconds.set(0);
      return;
    }

    const expiryTime = new Date(res.expiresAtUtc).getTime();
    const now = Date.now();
    const diffSeconds = Math.max(0, Math.floor((expiryTime - now) / 1000));
    this.remainingSeconds.set(diffSeconds);

    if (expiryTime <= now && !this.expiryLookupPending && !this.loading()) {
      this.expiryLookupPending = true;
      this.syncActive();
    }
  }

  private startPolling(): void {
    if (this.pollTimer) return;
    let heartbeat = 0;
    this.pollTimer = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) return;
      const current = this.activeReservation();
      if (current && (current.status === 'Held' || current.status === 'CheckedIn')) {
        if (!this.realtime.connected() || ++heartbeat % 4 === 0) this.syncActive();
      } else {
        this.stopPolling();
      }
    }, 4000);
  }

  private syncActive(change?: { id: string | null }): void {
    if (this.authService && !this.authService.isAuthenticated()) return;
    if (this.refreshPending) {
      if (change) this.queuedChange = change;
      return;
    }
    if (this.loading()) return;
    const previousId = this.activeReservation()?.id ?? change?.id;
    const owner = this.authService?.currentUser()?.id;
    this.refreshPending = true;
    this.getActive().pipe(
      switchMap((active) => !active && previousId ? this.getById(previousId) : EMPTY),
      catchError(() => EMPTY),
      finalize(() => {
        this.refreshPending = false;
        this.expiryLookupPending = false;
        const queued = this.queuedChange;
        this.queuedChange = null;
        if (queued && !this.destroyRef.destroyed) this.syncActive(queued);
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((closed) => {
      if (!this.activeReservation() && this.authService?.currentUser()?.id === owner &&
          ['Cancelled', 'Completed', 'Expired'].includes(closed.status)) {
        this.lastClosedReservation.set(closed);
      }
    });
  }

  private stopPolling(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }
}
