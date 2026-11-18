import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../../features/auth/services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const token = authService.session()?.accessToken;

  if (token && !req.headers.has('Authorization')) {
    const authReq = req.clone({
      headers: req.headers.set('Authorization', `Bearer ${token}`),
    });
    return next(authReq);
  }
  return next(req);
};
