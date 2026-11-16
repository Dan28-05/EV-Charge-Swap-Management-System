import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthenticatedUser, ProblemDetails } from '../models/authentication.models';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-profile',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss',
})
export class ProfileComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly user = signal<AuthenticatedUser | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly changingPassword = signal(false);
  protected readonly verificationChannel = signal<'email' | 'phone' | null>(null);
  protected readonly verificationBusy = signal(false);
  protected readonly notice = signal('');
  protected readonly error = signal('');

  protected readonly profileForm = new FormGroup({
    fullName: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(2), Validators.maxLength(160)],
    }),
  });
  protected readonly passwordForm = new FormGroup({
    currentPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    newPassword: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(8), Validators.maxLength(128)],
    }),
    confirmPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  protected readonly verificationForm = new FormGroup({
    code: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^\d{6}$/)],
    }),
  });

  ngOnInit(): void {
    if (!this.auth.isAuthenticated()) {
      this.router.navigate(['/login'], { queryParams: { returnUrl: '/profile' } });
      return;
    }
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set('');
    this.auth.profile().pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (user) => {
        this.user.set(user);
        this.profileForm.setValue({ fullName: user.fullName });
        this.auth.updateCachedUser(user);
      },
      error: (error: HttpErrorResponse) => this.handleError(error),
    });
  }

  protected saveProfile(): void {
    if (this.profileForm.invalid || this.saving()) {
      this.profileForm.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.notice.set('');
    this.error.set('');
    this.auth.updateProfile(this.profileForm.getRawValue().fullName.trim())
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (user) => {
          this.user.set(user);
          this.auth.updateCachedUser(user);
          this.notice.set('Đã cập nhật hồ sơ.');
        },
        error: (error: HttpErrorResponse) => this.handleError(error),
      });
  }

  protected changePassword(): void {
    if (this.passwordForm.invalid || this.changingPassword()) {
      this.passwordForm.markAllAsTouched();
      return;
    }
    const { currentPassword, newPassword, confirmPassword } = this.passwordForm.getRawValue();
    if (newPassword !== confirmPassword) {
      this.error.set('Mật khẩu xác nhận chưa khớp.');
      return;
    }
    this.changingPassword.set(true);
    this.notice.set('');
    this.error.set('');
    this.auth.changePassword({ currentPassword, newPassword })
      .pipe(finalize(() => this.changingPassword.set(false)))
      .subscribe({
        next: () => {
          this.auth.clearSession();
          this.router.navigate(['/login'], { queryParams: { passwordChanged: '1' } });
        },
        error: (error: HttpErrorResponse) => this.handleError(error),
      });
  }

  protected sendVerificationCode(channel: 'email' | 'phone'): void {
    if (this.verificationBusy()) return;
    this.verificationBusy.set(true);
    this.notice.set('');
    this.error.set('');
    this.auth.sendVerificationCode(channel)
      .pipe(finalize(() => this.verificationBusy.set(false)))
      .subscribe({
        next: () => {
          this.verificationChannel.set(channel);
          this.verificationForm.reset();
          this.notice.set(`Mã xác minh đã được gửi tới ${channel === 'email' ? 'email' : 'số điện thoại'} của bạn. Mã có hiệu lực 5 phút.`);
        },
        error: (error: HttpErrorResponse) => this.handleError(error),
      });
  }

  protected confirmVerificationCode(): void {
    const channel = this.verificationChannel();
    if (!channel || this.verificationForm.invalid || this.verificationBusy()) {
      this.verificationForm.markAllAsTouched();
      return;
    }
    this.verificationBusy.set(true);
    this.notice.set('');
    this.error.set('');
    this.auth.confirmVerificationCode(channel, this.verificationForm.controls.code.value)
      .pipe(finalize(() => this.verificationBusy.set(false)))
      .subscribe({
        next: () => {
          this.verificationChannel.set(null);
          this.verificationForm.reset();
          this.notice.set(`${channel === 'email' ? 'Email' : 'Số điện thoại'} đã được xác thực.`);
          this.reload();
        },
        error: (error: HttpErrorResponse) => this.handleError(error),
      });
  }

  protected cancelVerification(): void {
    this.verificationChannel.set(null);
    this.verificationForm.reset();
  }

  private handleError(error: HttpErrorResponse): void {
    if (error.status === 401) {
      this.auth.clearSession();
      this.router.navigate(['/login']);
      return;
    }
    const problem = error.error as ProblemDetails | null;
    this.error.set(problem?.detail ?? 'Không thể hoàn tất yêu cầu. Vui lòng thử lại.');
  }
}
