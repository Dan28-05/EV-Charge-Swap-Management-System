import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { Observable } from 'rxjs';
import {
  AuthenticatedUser,
  AuthenticationResponse,
  LoginCredentials,
  RegisterCustomerRequest,
  ChangePasswordRequest,
  ResetPasswordRequest,
} from '../models/authentication.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly platformId = inject(PLATFORM_ID);

  private readonly authStorageKey = 'evgo.auth';
  private readonly rememberedIdentifierKey = 'evgo.rememberedIdentifier';

  readonly session = signal<AuthenticationResponse | null>(this.loadInitialSession());
  readonly currentUser = computed<AuthenticatedUser | null>(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed<boolean>(() => !!this.session());

  login(credentials: LoginCredentials): Observable<AuthenticationResponse> {
    return this.http.post<AuthenticationResponse>('/api/auth/login', credentials);
  }

  register(request: RegisterCustomerRequest): Observable<void> {
    return this.http.post<void>('/api/auth/register', request);
  }

  profile(): Observable<AuthenticatedUser> {
    return this.http.get<AuthenticatedUser>('/api/users/me', { headers: this.authHeaders() });
  }

  updateProfile(fullName: string): Observable<AuthenticatedUser> {
    return this.http.put<AuthenticatedUser>('/api/users/me', { fullName }, { headers: this.authHeaders() });
  }

  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.http.post<void>('/api/users/me/change-password', request, { headers: this.authHeaders() });
  }

  sendVerificationCode(channel: 'email' | 'phone'): Observable<void> {
    return this.http.post<void>('/api/auth/verification/me/send', { channel }, {
      headers: this.authHeaders(),
    });
  }

  confirmVerificationCode(channel: 'email' | 'phone', code: string): Observable<void> {
    return this.http.post<void>('/api/auth/verification/me/confirm', { channel, code }, {
      headers: this.authHeaders(),
    });
  }

  forgotPassword(identifier: string): Observable<void> {
    return this.http.post<void>('/api/auth/password/forgot', { identifier });
  }

  resetPassword(request: ResetPasswordRequest): Observable<void> {
    return this.http.post<void>('/api/auth/password/reset', request);
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

  private authHeaders(): HttpHeaders {
    return new HttpHeaders({ Authorization: `Bearer ${this.session()?.accessToken ?? ''}` });
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
