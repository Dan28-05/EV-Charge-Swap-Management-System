import {
  HttpContextToken,
  HttpErrorResponse,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../../features/auth/services/auth.service';

export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);
export const IS_REFRESH_REQUEST = new HttpContextToken<boolean>(() => false);

const AUTH_EXCLUDED_URLS = [
  '/api/auth/login',
  '/api/auth/register',
  '/api/auth/refresh',
  '/api/auth/logout',
  '/api/auth/password/forgot',
  '/api/auth/password/reset',
];

function isAuthExcluded(url: string): boolean {
  return AUTH_EXCLUDED_URLS.some((excluded) => url.includes(excluded));
}

function attachToken(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({
    headers: req.headers.set('Authorization', `Bearer ${token}`),
  });
}

function navigateToLogin(router: Router): void {
  const currentUrl = router.url;
  const isAuthPage =
    currentUrl.startsWith('/login') ||
    currentUrl.startsWith('/register') ||
    currentUrl.startsWith('/forgot-password');

  if (!isAuthPage) {
    router.navigate(['/login'], {
      queryParams: currentUrl && currentUrl !== '/' ? { returnUrl: currentUrl } : undefined,
    });
  }
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (req.context.get(SKIP_AUTH)) {
    return next(req);
  }

  const token = authService.session()?.accessToken;
  const authReq = token && !req.headers.has('Authorization') ? attachToken(req, token) : req;

  return next(authReq).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      if (req.context.get(IS_REFRESH_REQUEST) || isAuthExcluded(req.url)) {
        return throwError(() => error);
      }

      if (!authService.session()?.refreshToken) {
        authService.clearSession();
        navigateToLogin(router);
        return throwError(() => error);
      }

      return authService.refreshToken().pipe(
        switchMap((newSession) => {
          const retriedReq = attachToken(req, newSession.accessToken);
          return next(retriedReq);
        }),
        catchError((refreshError) => {
          authService.clearSession();
          navigateToLogin(router);
          return throwError(() => refreshError);
        })
      );
    })
  );
};

