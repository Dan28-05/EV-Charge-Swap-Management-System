import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ProblemDetails } from '../../models/authentication.models';
import { AuthService } from '../../services/auth.service';

type Notice = { type: 'error' | 'success' | 'info'; text: string };

@Component({
  selector: 'app-login-form',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login-form.component.html',
  styleUrl: './login-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginFormComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly currentYear = new Date().getFullYear();
  protected readonly showPassword = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly notice = signal<Notice | null>(null);

  protected readonly form = new FormGroup({
    identifier: new FormControl(this.authService.getRememberedIdentifier(), {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(256)],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(128)],
    }),
    remember: new FormControl(Boolean(this.authService.getRememberedIdentifier()), {
      nonNullable: true,
    }),
  });

  protected isInvalid(controlName: 'identifier' | 'password'): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && (control.dirty || control.touched);
  }

  protected togglePassword(): void {
    this.showPassword.update((isVisible) => !isVisible);
  }

  protected showUnavailable(feature: string): void {
    this.notice.set({
      type: 'info',
      text: `${feature} đang được hoàn thiện trong giai đoạn tiếp theo.`,
    });
  }

  protected submit(): void {
    this.notice.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { identifier, password, remember } = this.form.getRawValue();
    const normalizedIdentifier = identifier.trim();
    this.isSubmitting.set(true);

    this.authService
      .login({ identifier: normalizedIdentifier, password })
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: (response) => {
          this.authService.saveSession(response, remember, normalizedIdentifier);
          this.form.controls.password.reset();
          this.notice.set({
            type: 'success',
            text: `Chào mừng ${response.user.fullName}. Đang chuyển hướng...`,
          });
          this.router.navigateByUrl('/');
        },
        error: (error: HttpErrorResponse) => {
          this.notice.set({ type: 'error', text: this.getErrorMessage(error) });
        },
      });
  }

  private getErrorMessage(error: HttpErrorResponse): string {
    if (error.status === 0) return 'Không thể kết nối máy chủ. Vui lòng kiểm tra API và thử lại.';
    if (error.status === 401)
      return 'Thông tin đăng nhập chưa đúng hoặc tài khoản chưa được xác minh.';

    const problem = error.error as ProblemDetails | null;
    return problem?.detail ?? problem?.title ?? 'Đăng nhập chưa thành công. Vui lòng thử lại.';
  }
}
