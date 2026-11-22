import { inject } from '@angular/core';
import { CanActivateFn, CanMatchFn, Router } from '@angular/router';
import { AuthService } from '../../features/auth/services/auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  if (auth.isAuthenticated()) {
    return true;
  }

  const router = inject(Router);
  return router.createUrlTree(['/login'], {
    queryParams: state.url && state.url !== '/' ? { returnUrl: state.url } : undefined,
  });
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (!auth.isAuthenticated()) {
    return true;
  }

  return inject(Router).parseUrl(auth.home().path);
};

export function roleGuard(allowedRoles: readonly string[]): CanMatchFn {
  return (_route, segments) => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.isAuthenticated()) {
      const targetUrl = '/' + segments.map((s) => s.path).join('/');
      return router.createUrlTree(['/login'], {
        queryParams: targetUrl !== '/' ? { returnUrl: targetUrl } : undefined,
      });
    }

    const userRoles = auth.currentUser()?.roles ?? [];
    if (allowedRoles.some((role) => userRoles.includes(role))) {
      return true;
    }

    return router.parseUrl(auth.home().path);
  };
}
