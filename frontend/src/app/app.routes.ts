import { inject } from '@angular/core';
import { Router, Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard } from './core/guards/auth.guards';
import { AuthService } from './features/auth/services/auth.service';

export const routes: Routes = [
  {
    path: '', pathMatch: 'full',
    canMatch: [() => {
      const home = inject(AuthService).home();
      return home.path === '/' || inject(Router).parseUrl(home.path);
    }],
    loadComponent: () => import('./features/station-finder/station-finder-page.component').then(m => m.StationFinderPageComponent),
    title: 'Tìm trạm — EVGo',
  },
  {
    path: '',
    loadComponent: () => import('./features/auth/login-page/login-page.component').then(m => m.LoginPageComponent),
    canActivate: [guestGuard],
    children: [
      { path: 'login', loadComponent: () => import('./features/auth/login-page/login-form/login-form.component').then(m => m.LoginFormComponent), title: 'Đăng nhập — EVGo' },
      { path: 'register', loadComponent: () => import('./features/auth/login-page/register-form/register-form.component').then(m => m.RegisterFormComponent), title: 'Đăng ký — EVGo' },
      { path: 'forgot-password', loadComponent: () => import('./features/auth/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent), title: 'Quên mật khẩu — EVGo' },
    ],
  },
  { path: 'profile', canActivate: [authGuard], loadComponent: () => import('./features/auth/profile/profile.component').then(m => m.ProfileComponent), title: 'Hồ sơ cá nhân — EVGo' },
  { path: 'notifications', canActivate: [authGuard], loadComponent: () => import('./features/notifications/notifications.component').then(m => m.NotificationsComponent), title: 'Thông báo — EVGo' },
  { path: 'invoices', canActivate: [authGuard], loadComponent: () => import('./features/invoices/invoices.component').then(m => m.InvoicesComponent), title: 'Hóa đơn — EVGo' },
  {
    path: 'admin',
    canMatch: [roleGuard(['Admin'])],
    loadComponent: () =>
      import('./features/admin/admin-dashboard/admin-dashboard.component').then(
        (m) => m.AdminDashboardComponent,
      ),
    title: 'Quản trị Hệ thống — EVGo',
  },
  {
    path: 'operator/dashboard',
    canMatch: [roleGuard(['Operator'])],
    loadComponent: () =>
      import('./features/operator/operator-dashboard/operator-dashboard.component').then(
        (m) => m.OperatorDashboardComponent,
      ),
    title: 'Dashboard Vận Hành — EVGo',
  },
  {
    path: 'operator',
    redirectTo: 'operator/dashboard',
    pathMatch: 'full',
  },
  {
    path: 'operator/check-in',
    canMatch: [roleGuard(['Operator'])],
    loadComponent: () =>
      import('./features/reservations/operator-checkin/operator-checkin.component').then(
        (m) => m.OperatorCheckinComponent,
      ),
    title: 'Quầy Vận Hành & Check-in — EVGo',
  },
  { path: '**', redirectTo: '' },
];
