import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, tap, throwError } from 'rxjs';
import {
  CheckInRequest,
  HoldRequest,
  ReservationResponse,
} from '../models/reservation.models';

@Injectable({ providedIn: 'root' })
export class ReservationService {
  private readonly http = inject(HttpClient);

  readonly activeReservation = signal<ReservationResponse | null>(null);
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
    this.startCountdownLoop();
  }

  hold(request: HoldRequest): Observable<ReservationResponse> {
    this.loading.set(true);
    return this.http.post<ReservationResponse>('/api/reservations', request).pipe(
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
    return this.http.get<ReservationResponse>('/api/reservations/active').pipe(
      map((res) => {
        this.setActive(res);
        return res;
      }),
      catchError((err: HttpErrorResponse) => {
        if (err.status === 404) {
          this.setActive(null);
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
    this.activeReservation.set(reservation);
    this.updateCountdown();

    if (reservation && reservation.status === 'Held') {
      this.startPolling();
    } else {
      this.stopPolling();
    }
  }

  private startCountdownLoop(): void {
    if (this.countdownTimer) clearInterval(this.countdownTimer);
    this.countdownTimer = setInterval(() => {
      this.updateCountdown();
    }, 1000);
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

    if (diffSeconds <= 0 && res.status === 'Held') {
      // Refresh to confirm expired
      this.getActive().subscribe();
    }
  }

  private startPolling(): void {
    if (this.pollTimer) return;
    this.pollTimer = setInterval(() => {
      if (this.activeReservation()?.status === 'Held') {
        this.getActive().subscribe();
      } else {
        this.stopPolling();
      }
    }, 8000);
  }

  private stopPolling(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }
}
