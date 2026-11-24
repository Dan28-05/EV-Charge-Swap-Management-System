import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthenticatedUser, ProblemDetails } from '../models/authentication.models';
import { AuthService } from '../services/auth.service';

export type ProfileTab = 'personal' | 'security' | 'mobility';

const STRONG_PASSWORD_PATTERN = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$/;

@Component({
  selector: 'app-profile',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfileComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private copiedTimer?: ReturnType<typeof setTimeout>;

  protected readonly user = signal<AuthenticatedUser | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly changingPassword = signal(false);
  protected readonly verificationChannel = signal<'email' | 'phone' | null>(null);
  protected readonly verificationBusy = signal(false);
  protected readonly notice = signal('');
  protected readonly error = signal('');

  /* UI Interactive States */
  protected readonly activeTab = signal<ProfileTab>('personal');
  protected readonly isEditingName = signal(false);
  protected readonly showCurrentPassword = signal(false);
  protected readonly showNewPassword = signal(false);
  protected readonly showConfirmPassword = signal(false);
  protected readonly copiedId = signal(false);

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
      validators: [
        Validators.required,
        Validators.minLength(8),
        Validators.maxLength(128),
        Validators.pattern(STRONG_PASSWORD_PATTERN),
      ],
    }),
    confirmPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly verificationForm = new FormGroup({
    code: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^\d{6}$/)],
    }),
  });

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.copiedTimer));
  }

  ngOnInit(): void {
    if (!this.auth.isAuthenticated()) {
      this.router.navigate(['/login'], { queryParams: { returnUrl: '/profile' } });
      return;
    }
    const requestedTab = this.route.snapshot.queryParamMap.get('tab');
    if (requestedTab === 'personal' || requestedTab === 'security' || requestedTab === 'mobility') {
      this.activeTab.set(requestedTab);
    }
    this.reload();
  }

  protected setTab(tab: ProfileTab): void {
    this.activeTab.set(tab);
    this.notice.set('');
    this.error.set('');
  }

  protected startEditName(): void {
    if (this.user()) {
      this.profileForm.setValue({ fullName: this.user()!.fullName });
    }
    this.isEditingName.set(true);
  }

  protected cancelEditName(): void {
    if (this.user()) {
      this.profileForm.setValue({ fullName: this.user()!.fullName });
    }
    this.isEditingName.set(false);
  }

  protected toggleShowPassword(field: 'current' | 'new' | 'confirm'): void {
    if (field === 'current') this.showCurrentPassword.update((v) => !v);
    if (field === 'new') this.showNewPassword.update((v) => !v);
    if (field === 'confirm') this.showConfirmPassword.update((v) => !v);
  }

  protected copyAccountId(id: string): void {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(id);
      this.copiedId.set(true);
      clearTimeout(this.copiedTimer);
      this.copiedTimer = setTimeout(() => this.copiedId.set(false), 2000);
    }
  }

  protected logout(): void {
    this.auth.logout().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.router.navigate(['/login']),
      error: () => this.router.navigate(['/login']),
    });
  }

  protected getInitials(name: string): string {
    if (!name) return 'EV';
    const parts = name.trim().split(/\s+/);
    if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  }

  protected getRoleLabel(roles?: string[]): string {
    if (!roles || roles.length === 0) return 'Hội viên EVGo';
    if (roles.includes('Admin')) return 'Quản trị viên hệ thống';
    if (roles.includes('Operator')) return 'Điều hành viên trạm sạc';
    if (roles.includes('StationStaff')) return 'Kỹ thuật viên trạm sạc';
    return 'Hội viên EVGo';
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set('');
    this.auth
      .profile()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
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
    this.auth
      .updateProfile(this.profileForm.getRawValue().fullName.trim())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (user) => {
          this.user.set(user);
          this.auth.updateCachedUser(user);
          this.isEditingName.set(false);
          this.notice.set('Thông tin hồ sơ đã được cập nhật thành công.');
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
      this.error.set('Mật khẩu xác nhận chưa khớp với mật khẩu mới.');
      return;
    }
    this.changingPassword.set(true);
    this.notice.set('');
    this.error.set('');
    this.auth
      .changePassword({ currentPassword, newPassword })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.changingPassword.set(false)),
      )
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
    this.auth
      .sendVerificationCode(channel)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.verificationBusy.set(false)),
      )
      .subscribe({
        next: () => {
          this.verificationChannel.set(channel);
          this.verificationForm.reset();
          this.notice.set(
            `Mã xác minh gồm 6 số đã được gửi tới ${
              channel === 'email' ? 'email' : 'số điện thoại'
            } của bạn. Mã có hiệu lực trong 5 phút.`,
          );
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
    this.auth
      .confirmVerificationCode(channel, this.verificationForm.controls.code.value)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.verificationBusy.set(false)),
      )
      .subscribe({
        next: () => {
          this.verificationChannel.set(null);
          this.verificationForm.reset();
          this.notice.set(`${channel === 'email' ? 'Email' : 'Số điện thoại'} đã được xác thực thành công.`);
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
    this.error.set(problem?.detail ?? 'Không thể hoàn tất yêu cầu. Vui lòng kiểm tra lại.');
  }
}
