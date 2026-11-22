import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router, UrlSegment, UrlTree } from '@angular/router';
import { AuthService } from '../../features/auth/services/auth.service';
import { AuthenticationResponse } from '../../features/auth/models/authentication.models';
import { authGuard, guestGuard, roleGuard } from './auth.guards';

describe('auth guards', () => {
  let authService: AuthService;
  let router: Router;

  const makeSession = (roles: string[]): AuthenticationResponse => ({
    tokenType: 'Bearer',
    accessToken: 'token',
    accessTokenExpiresAtUtc: new Date().toISOString(),
    refreshToken: 'refresh',
    refreshTokenExpiresAtUtc: new Date().toISOString(),
    user: {
      id: 'u-1',
      fullName: 'Test User',
      email: 'user@evgo.vn',
      phoneNumber: '0901234567',
      emailConfirmed: true,
      phoneNumberConfirmed: true,
      roles,
    },
  });

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    authService = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it('authGuard redirects unauthenticated users to /login with returnUrl', () => {
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url: '/profile' } as never)
    );
    expect(result instanceof UrlTree).toBe(true);
    expect(router.serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Fprofile');
  });

  it('authGuard allows authenticated users', () => {
    authService.saveSession(makeSession(['Customer']), false, 'user@evgo.vn');
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url: '/profile' } as never)
    );
    expect(result).toBe(true);
  });

  it('guestGuard redirects logged-in Operator to /operator/dashboard', () => {
    authService.saveSession(makeSession(['Operator']), false, 'op@evgo.vn');
    const result = TestBed.runInInjectionContext(() =>
      guestGuard({} as never, { url: '/login' } as never)
    );
    expect(result instanceof UrlTree).toBe(true);
    expect(router.serializeUrl(result as UrlTree)).toBe('/operator/dashboard');
  });

  it('roleGuard rejects anonymous users and redirects to /login', () => {
    const guard = roleGuard(['Operator']);
    const result = TestBed.runInInjectionContext(() =>
      guard({} as never, [new UrlSegment('operator', {}), new UrlSegment('check-in', {})])
    );
    expect(result instanceof UrlTree).toBe(true);
    expect(router.serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Foperator%2Fcheck-in');
  });

  it('roleGuard rejects Customer or Admin on Operator-only routes and allows Operator', () => {
    const guard = roleGuard(['Operator']);

    authService.saveSession(makeSession(['Customer']), false, 'c@evgo.vn');
    const customerResult = TestBed.runInInjectionContext(() => guard({} as never, []));
    expect(customerResult instanceof UrlTree).toBe(true);
    expect(router.serializeUrl(customerResult as UrlTree)).toBe('/');

    authService.saveSession(makeSession(['Admin']), false, 'a@evgo.vn');
    const adminResult = TestBed.runInInjectionContext(() => guard({} as never, []));
    expect(adminResult instanceof UrlTree).toBe(true);

    authService.saveSession(makeSession(['Operator']), false, 'o@evgo.vn');
    const opResult = TestBed.runInInjectionContext(() => guard({} as never, []));
    expect(opResult).toBe(true);
  });
});
