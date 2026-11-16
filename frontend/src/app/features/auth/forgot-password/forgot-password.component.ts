import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ProblemDetails } from '../models/authentication.models';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.scss',
})
export class ForgotPasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly codeRequested = signal(false);
  protected readonly busy = signal(false);
  protected readonly notice = signal('');
  protected readonly error = signal('');
  protected readonly form = new FormGroup({
    identifier: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d{6}$/)] }),
    newPassword: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(8), Validators.maxLength(128)] }),
    confirmPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected requestCode(): void {
    if (this.form.controls.identifier.invalid || this.busy()) {
      this.form.controls.identifier.markAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.auth.forgotPassword(this.form.controls.identifier.value.trim())
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => {
          this.codeRequested.set(true);
          this.notice.set('Nếu tài khoản và kênh liên hệ đã được xác minh, mã 6 số sẽ được gửi tới đó. Mã có hiệu lực 5 phút.');
        },
        error: (error: HttpErrorResponse) => this.handleError(error),
      });
  }

  protected resetPassword(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const { identifier, code, newPassword, confirmPassword } = this.form.getRawValue();
    if (newPassword !== confirmPassword) {
      this.error.set('Mật khẩu xác nhận chưa khớp.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.auth.resetPassword({ identifier: identifier.trim(), code, newPassword })
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => {
          this.auth.clearSession();
          this.router.navigate(['/login'], { queryParams: { passwordReset: '1' } });
        },
        error: (error: HttpErrorResponse) => this.handleError(error),
      });
  }

  private handleError(error: HttpErrorResponse): void {
    const problem = error.error as ProblemDetails | null;
    this.error.set(error.status === 503
      ? 'Dịch vụ gửi mã chưa được cấu hình. Vui lòng liên hệ bộ phận hỗ trợ.'
      : problem?.detail ?? 'Không thể xử lý yêu cầu. Vui lòng thử lại.');
  }
}
