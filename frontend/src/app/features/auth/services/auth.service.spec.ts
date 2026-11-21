import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AuthService } from './auth.service';
import { AuthenticationResponse } from '../models/authentication.models';

describe('AuthService - Refresh Token & Session Management', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const mockInitialSession: AuthenticationResponse = {
    tokenType: 'Bearer',
    accessToken: 'old-access-token',
    accessTokenExpiresAtUtc: new Date(Date.now() + 60000).toISOString(),
    refreshToken: 'valid-refresh-token',
    refreshTokenExpiresAtUtc: new Date(Date.now() + 86400000).toISOString(),
    user: {
      id: 'user-1',
      fullName: 'Nguyen Van A',
      email: 'user@evgo.vn',
      phoneNumber: '0901234567',
      emailConfirmed: true,
      phoneNumberConfirmed: true,
      roles: ['Customer'],
    },
  };

  const mockRefreshedSession: AuthenticationResponse = {
    ...mockInitialSession,
    accessToken: 'new-refreshed-access-token',
    refreshToken: 'new-rotated-refresh-token',
  };

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [AuthService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('should refresh token successfully and update session signal and storage', () => {
    service.saveSession(mockInitialSession, true, 'user@evgo.vn');
    expect(service.session()?.accessToken).toBe('old-access-token');

    let result: AuthenticationResponse | undefined;
    service.refreshToken().subscribe((res) => {
      result = res;
    });

    const req = httpMock.expectOne('/api/auth/refresh');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ refreshToken: 'valid-refresh-token' });

    req.flush(mockRefreshedSession);

    expect(result).toEqual(mockRefreshedSession);
    expect(service.session()?.accessToken).toBe('new-refreshed-access-token');
    expect(service.session()?.refreshToken).toBe('new-rotated-refresh-token');

    const stored = JSON.parse(localStorage.getItem('evgo.auth')!);
    expect(stored.accessToken).toBe('new-refreshed-access-token');
  });

  it('should share single in-flight request when multiple callers request refresh simultaneously', () => {
    service.saveSession(mockInitialSession, false, 'user@evgo.vn');

    let res1: AuthenticationResponse | undefined;
    let res2: AuthenticationResponse | undefined;
    let res3: AuthenticationResponse | undefined;

    // Simulate 3 concurrent refresh requests
    service.refreshToken().subscribe((res) => (res1 = res));
    service.refreshToken().subscribe((res) => (res2 = res));
    service.refreshToken().subscribe((res) => (res3 = res));

    // Only ONE HTTP request should be sent to backend
    const reqs = httpMock.match('/api/auth/refresh');
    expect(reqs.length).toBe(1);

    reqs[0].flush(mockRefreshedSession);

    expect(res1?.accessToken).toBe('new-refreshed-access-token');
    expect(res2?.accessToken).toBe('new-refreshed-access-token');
    expect(res3?.accessToken).toBe('new-refreshed-access-token');
  });

  it('should clear session and propagate error if refresh token request fails', () => {
    service.saveSession(mockInitialSession, true, 'user@evgo.vn');

    let errorMessage = '';
    service.refreshToken().subscribe({
      next: () => {
        throw new Error('Should have failed');
      },
      error: (err) => {
        errorMessage = err.statusText || 'Error';
      },
    });

    const req = httpMock.expectOne('/api/auth/refresh');
    req.flush({ title: 'Invalid Token' }, { status: 401, statusText: 'Unauthorized' });

    expect(service.session()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('evgo.auth')).toBeNull();
  });

  it('should throw immediately if no session or refresh token exists', () => {
    service.clearSession();

    let errorThrown = false;
    service.refreshToken().subscribe({
      next: () => {
        throw new Error('Should not succeed');
      },
      error: () => {
        errorThrown = true;
      },
    });

    httpMock.expectNone('/api/auth/refresh');
    expect(errorThrown).toBe(true);
  });

  it('should call server logout endpoint with refresh token and clear local session', () => {
    service.saveSession(mockInitialSession, true, 'user@evgo.vn');

    let loggedOut = false;
    service.logout().subscribe(() => {
      loggedOut = true;
    });

    const req = httpMock.expectOne('/api/auth/logout');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ refreshToken: 'valid-refresh-token' });

    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(loggedOut).toBe(true);
    expect(service.session()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('evgo.auth')).toBeNull();
  });
});
