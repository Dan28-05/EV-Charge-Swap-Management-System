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
    ],
  },
  { path: '**', redirectTo: '' },
];
