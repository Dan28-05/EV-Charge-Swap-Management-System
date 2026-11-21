import { Routes } from '@angular/router';
import { LoginPageComponent } from './features/auth/login-page/login-page.component';
import { LoginFormComponent } from './features/auth/login-page/login-form/login-form.component';
import { RegisterFormComponent } from './features/auth/login-page/register-form/register-form.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./features/station-finder/station-finder-page.component').then(m => m.StationFinderPageComponent), title: 'Tìm trạm — EVGo' },
  {
    path: '',
    component: LoginPageComponent,
    children: [
      { path: 'login', component: LoginFormComponent, title: 'Đăng nhập — EVGo' },
      { path: 'register', component: RegisterFormComponent, title: 'Đăng ký — EVGo' },
      { path: 'forgot-password', loadComponent: () => import('./features/auth/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent), title: 'Quên mật khẩu — EVGo' },
    ],
  },
  { path: 'profile', loadComponent: () => import('./features/auth/profile/profile.component').then(m => m.ProfileComponent), title: 'Hồ sơ cá nhân — EVGo' },
  { path: 'notifications', loadComponent: () => import('./features/notifications/notifications.component').then(m => m.NotificationsComponent), title: 'Thông báo — EVGo' },
  { path: 'invoices', loadComponent: () => import('./features/invoices/invoices.component').then(m => m.InvoicesComponent), title: 'Hóa đơn — EVGo' },
  {
    path: 'operator/dashboard',
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
    loadComponent: () =>
      import('./features/reservations/operator-checkin/operator-checkin.component').then(
        (m) => m.OperatorCheckinComponent,
      ),
    title: 'Quầy Vận Hành & Check-in — EVGo',
  },
  { path: '**', redirectTo: '' },
];
