import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

describe('App', () => {
  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    }).compileComponents();
  });

  it('should create the app', () => {
    expect(TestBed.createComponent(App).componentInstance).toBeTruthy();
  });

  it('should render the Vietnamese login experience', async () => {
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/login');
    fixture.detectChanges();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Đi xa hơn');
    expect(compiled.querySelector('h2')?.textContent).toContain('Chào mừng trở lại');
    expect(compiled.querySelector('form')).toBeTruthy();
    expect(compiled.querySelector('.auth-layout > app-brand-showcase')).toBeTruthy();
    expect(compiled.querySelector('.auth-layout > .auth-content app-login-form')).toBeTruthy();
  });

  it('should render the complete registration form', async () => {
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/register');
    fixture.detectChanges();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('h2')?.textContent).toContain('Bắt đầu cùng EVGo');
    expect(compiled.querySelector('#fullName')).toBeTruthy();
    expect(compiled.querySelector('#email')).toBeTruthy();
    expect(compiled.querySelector('#phoneNumber')).toBeTruthy();
    expect(compiled.querySelector('#registerPassword')).toBeTruthy();
    expect(compiled.querySelector('#passwordConfirmation')).toBeTruthy();
  });
});
