import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { authInterceptor, SKIP_AUTH } from './auth.interceptor';
import { AuthService } from '../../features/auth/services/auth.service';
import { AuthenticationResponse } from '../../features/auth/models/authentication.models';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let authService: AuthService;
  let router: Router;

  const mockSession: AuthenticationResponse = {
    tokenType: 'Bearer',
    accessToken: 'initial-access-token',
    accessTokenExpiresAtUtc: new Date().toISOString(),
    refreshToken: 'initial-refresh-token',
    refreshTokenExpiresAtUtc: new Date().toISOString(),
    user: {
      id: 'u-1',
      fullName: 'Test User',
      email: 'test@evgo.vn',
      phoneNumber: '0901234567',
      emailConfirmed: true,
      phoneNumberConfirmed: true,
      roles: ['Customer'],
    },
  };

  const mockRefreshedSession: AuthenticationResponse = {
    ...mockSession,
    accessToken: 'refreshed-access-token',
    refreshToken: 'refreshed-refresh-token',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    authService = TestBed.inject(AuthService);
    router = TestBed.inject(Router);

    vi.spyOn(router, 'navigate').mockImplementation(() => Promise.resolve(true));
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should automatically attach Authorization Bearer header when user is logged in', () => {
    authService.saveSession(mockSession, false, 'test@evgo.vn');

    http.get('/api/users/me').subscribe();

    const req = httpMock.expectOne('/api/users/me');
    expect(req.request.headers.get('Authorization')).toBe('Bearer initial-access-token');
    req.flush(mockSession.user);
  });

  it('should not attach Authorization header if SKIP_AUTH is provided in HttpContext', () => {
    authService.saveSession(mockSession, false, 'test@evgo.vn');

    http
      .get('/api/public/stations', {
        context: new HttpContext().set(SKIP_AUTH, true),
      })
      .subscribe();

    const req = httpMock.expectOne('/api/public/stations');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });

  it('should automatically refresh token on 401 error and retry original request with new token', () => {
    authService.saveSession(mockSession, false, 'test@evgo.vn');
    vi.spyOn(authService, 'refreshToken').mockReturnValue(of(mockRefreshedSession));

    let finalData: unknown;
    http.get('/api/users/me').subscribe((res) => {
      finalData = res;
    });

    // 1. Initial request fails with 401 Unauthorized
    const initialReq = httpMock.expectOne('/api/users/me');
    expect(initialReq.request.headers.get('Authorization')).toBe('Bearer initial-access-token');
    initialReq.flush({ title: 'Token Expired' }, { status: 401, statusText: 'Unauthorized' });

    // Expect authService.refreshToken to be called
    expect(authService.refreshToken).toHaveBeenCalled();

    // 2. Retried request with new access token
    const retriedReq = httpMock.expectOne('/api/users/me');
    expect(retriedReq.request.headers.get('Authorization')).toBe('Bearer refreshed-access-token');
    retriedReq.flush(mockSession.user);

    expect(finalData).toEqual(mockSession.user);
  });

  it('should clear session and redirect to /login if refresh token fails with error', () => {
    authService.saveSession(mockSession, false, 'test@evgo.vn');
    vi.spyOn(authService, 'refreshToken').mockReturnValue(
      throwError(() => new Error('Refresh failed'))
    );
    const clearSessionSpy = vi.spyOn(authService, 'clearSession');

    let receivedError: unknown;
    http.get('/api/reservations').subscribe({
      next: () => {
        throw new Error('Should have failed');
      },
      error: (err) => {
        receivedError = err;
      },
    });

    const initialReq = httpMock.expectOne('/api/reservations');
    initialReq.flush({ title: 'Token Expired' }, { status: 401, statusText: 'Unauthorized' });

    expect(clearSessionSpy).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login'], expect.anything());
    expect(receivedError).toBeDefined();
  });

  it('should not attempt refresh token on 401 for excluded endpoints like /api/auth/login', () => {
    const refreshSpy = vi.spyOn(authService, 'refreshToken');

    let loginError: unknown;
    http.post('/api/auth/login', { identifier: 'bad', password: 'bad' }).subscribe({
      next: () => {
        throw new Error('Should have failed');
      },
      error: (err) => {
        loginError = err;
      },
    });

    const loginReq = httpMock.expectOne('/api/auth/login');
    loginReq.flush({ title: 'Invalid Credentials' }, { status: 401, statusText: 'Unauthorized' });

    expect(refreshSpy).not.toHaveBeenCalled();
    expect(loginError).toBeDefined();
  });
});
