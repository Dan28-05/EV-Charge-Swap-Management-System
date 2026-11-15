import { HttpClient } from '@angular/common/http';
import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { Observable } from 'rxjs';
import {
  AuthenticatedUser,
  AuthenticationResponse,
  LoginCredentials,
  RegisterCustomerRequest,
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
