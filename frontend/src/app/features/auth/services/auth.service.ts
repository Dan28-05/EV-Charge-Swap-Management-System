import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { Observable, catchError, finalize, of, shareReplay, tap, throwError } from 'rxjs';
import {
  AuthenticatedUser,
  AuthenticationResponse,
  LoginCredentials,
  RegisterCustomerRequest,
  ChangePasswordRequest,
  ResetPasswordRequest,
  RefreshTokenRequest,
} from '../models/authentication.models';
import { IS_REFRESH_REQUEST, SKIP_AUTH } from '../../../core/interceptors/auth.interceptor';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly platformId = inject(PLATFORM_ID);

  private readonly authStorageKey = 'evgo.auth';
  private readonly rememberedIdentifierKey = 'evgo.rememberedIdentifier';

  private refreshTokenInProgress$: Observable<AuthenticationResponse> | null = null;

  readonly session = signal<AuthenticationResponse | null>(this.loadInitialSession());
  readonly currentUser = computed<AuthenticatedUser | null>(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed<boolean>(() => !!this.session());
  readonly home = computed(() => {
    const roles = this.currentUser()?.roles ?? [];
    if (roles.includes('Admin')) {
      return { path: '/admin', label: 'Quản trị hệ thống' };
    }
    if (roles.includes('Operator')) {
      return { path: '/operator/dashboard', label: 'Vận hành trạm' };
    }
    return { path: '/', label: 'Bản đồ trạm' };
  });
  readonly isOperator = computed<boolean>(() => {
    const roles = this.currentUser()?.roles ?? [];
    return roles.includes('Operator');
  });
  readonly isAdmin = computed<boolean>(() => {
    const roles = this.currentUser()?.roles ?? [];
    return roles.includes('Admin');
  });

  login(credentials: LoginCredentials): Observable<AuthenticationResponse> {
    return this.http.post<AuthenticationResponse>('/api/auth/login', credentials);
  }

  register(request: RegisterCustomerRequest): Observable<void> {
    return this.http.post<void>('/api/auth/register', request);
  }

  profile(): Observable<AuthenticatedUser> {
    return this.http.get<AuthenticatedUser>('/api/users/me');
  }

  updateProfile(fullName: string): Observable<AuthenticatedUser> {
    return this.http.put<AuthenticatedUser>('/api/users/me', { fullName });
  }

  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.http.post<void>('/api/users/me/change-password', request);
  }

  sendVerificationCode(channel: 'email' | 'phone'): Observable<void> {
    return this.http.post<void>('/api/auth/verification/me/send', { channel });
  }

  confirmVerificationCode(channel: 'email' | 'phone', code: string): Observable<void> {
    return this.http.post<void>('/api/auth/verification/me/confirm', { channel, code });
  }

  forgotPassword(identifier: string): Observable<void> {
    return this.http.post<void>('/api/auth/password/forgot', { identifier });
  }

  resetPassword(request: ResetPasswordRequest): Observable<void> {
    return this.http.post<void>('/api/auth/password/reset', request);
  }

  refreshToken(): Observable<AuthenticationResponse> {
    if (this.refreshTokenInProgress$) {
      return this.refreshTokenInProgress$;
    }

    const currentSession = this.session();
    const token = currentSession?.refreshToken;
    if (!token) {
      this.clearSession();
      return throwError(() => new Error('Không tìm thấy refresh token trong phiên đăng nhập.'));
    }

    const refreshPayload: RefreshTokenRequest = { refreshToken: token };

    this.refreshTokenInProgress$ = this.http
      .post<AuthenticationResponse>('/api/auth/refresh', refreshPayload, {
        context: new HttpContext().set(SKIP_AUTH, true).set(IS_REFRESH_REQUEST, true),
      })
      .pipe(
        tap((newSession) => {
          this.session.set(newSession);
          if (isPlatformBrowser(this.platformId)) {
            const storage = localStorage.getItem(this.authStorageKey)
              ? localStorage
              : sessionStorage;
            storage.setItem(this.authStorageKey, JSON.stringify(newSession));
          }
        }),
        catchError((error) => {
          this.clearSession();
          return throwError(() => error);
        }),
        finalize(() => {
          this.refreshTokenInProgress$ = null;
        }),
        shareReplay({ bufferSize: 1, refCount: false })
      );

    return this.refreshTokenInProgress$;
  }

  logout(): Observable<void> {
    const token = this.session()?.refreshToken;
    this.clearSession();

    if (!token) {
      return of(void 0);
    }

    const logoutPayload: RefreshTokenRequest = { refreshToken: token };

    return this.http
      .post<void>('/api/auth/logout', logoutPayload, {
        context: new HttpContext().set(SKIP_AUTH, true).set(IS_REFRESH_REQUEST, true),
      })
      .pipe(
        catchError(() => of(void 0))
      );
  }

  updateCachedUser(user: AuthenticatedUser): void {
    const session = this.session();
    if (!session) return;
    const updated = { ...session, user };
    this.session.set(updated);
    if (!isPlatformBrowser(this.platformId)) return;
    const storage = localStorage.getItem(this.authStorageKey) ? localStorage : sessionStorage;
    storage.setItem(this.authStorageKey, JSON.stringify(updated));
  }

  saveSession(response: AuthenticationResponse, remember: boolean, identifier: string): void {
    this.session.set(response);
    if (!isPlatformBrowser(this.platformId)) return;

    const preferredStorage = remember ? localStorage : sessionStorage;
    const otherStorage = remember ? sessionStorage : localStorage;

    preferredStorage.setItem(this.authStorageKey, JSON.stringify(response));
    otherStorage.removeItem(this.authStorageKey);

    if (remember) {
      localStorage.setItem(this.rememberedIdentifierKey, identifier);
    } else {
      localStorage.removeItem(this.rememberedIdentifierKey);
    }
  }

  clearSession(): void {
    this.session.set(null);
    if (!isPlatformBrowser(this.platformId)) return;

    localStorage.removeItem(this.authStorageKey);
    sessionStorage.removeItem(this.authStorageKey);
  }

  getRememberedIdentifier(): string {
    if (!isPlatformBrowser(this.platformId)) return '';
    return localStorage.getItem(this.rememberedIdentifierKey) ?? '';
  }

  private loadInitialSession(): AuthenticationResponse | null {
    if (!isPlatformBrowser(this.platformId)) return null;

    try {
      const stored =
        localStorage.getItem(this.authStorageKey) || sessionStorage.getItem(this.authStorageKey);
      return stored ? (JSON.parse(stored) as AuthenticationResponse) : null;
    } catch {
      return null;
    }
  }
}
