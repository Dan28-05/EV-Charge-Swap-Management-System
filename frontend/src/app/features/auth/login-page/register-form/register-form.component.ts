import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ProblemDetails } from '../../models/authentication.models';
import { AuthService } from '../../services/auth.service';

function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  const password = group.get('password')?.value;
  const confirmation = group.get('passwordConfirmation')?.value;
  return password === confirmation ? null : { passwordsMismatch: true };
}

@Component({
  selector: 'app-register-form',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register-form.component.html',
  styleUrl: './register-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterFormComponent {
  private readonly authService = inject(AuthService);

  protected readonly currentYear = new Date().getFullYear();
  protected readonly isSubmitting = signal(false);
  protected readonly showPassword = signal(false);
  protected readonly registeredEmail = signal<string | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = new FormGroup(
    {
      fullName: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.minLength(2), Validators.maxLength(160)],
      }),
      email: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.email, Validators.maxLength(256)],
      }),
      phoneNumber: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.pattern(/^(?:0|\+84)(?:3|5|7|8|9)\d{8}$/)],
      }),
      password: new FormControl('', {
        nonNullable: true,
        validators: [
          Validators.required,
          Validators.pattern(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,128}$/),
        ],
      }),
      passwordConfirmation: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required],
      }),
      acceptedTerms: new FormControl(false, {
        nonNullable: true,
        validators: [Validators.requiredTrue],
      }),
    },
    { validators: passwordsMatch },
  );

  protected isInvalid(
    controlName: 'fullName' | 'email' | 'phoneNumber' | 'password' | 'passwordConfirmation',
  ): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && (control.dirty || control.touched);
  }

  protected confirmationInvalid(): boolean {
    const confirmation = this.form.controls.passwordConfirmation;
    return (
      (confirmation.invalid || this.form.hasError('passwordsMismatch')) &&
      (confirmation.dirty || confirmation.touched)
    );
  }

  protected togglePassword(): void {
    this.showPassword.update((isVisible) => !isVisible);
  }

  protected submit(): void {
    this.errorMessage.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { fullName, email, phoneNumber, password } = this.form.getRawValue();
    this.isSubmitting.set(true);

    this.authService
      .register({
        fullName: fullName.trim(),
        email: email.trim(),
        phoneNumber: phoneNumber.trim(),
        password,
      })
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: () => this.registeredEmail.set(email.trim()),
        error: (error: HttpErrorResponse) => this.errorMessage.set(this.getErrorMessage(error)),
      });
  }

  private getErrorMessage(error: HttpErrorResponse): string {
    if (error.status === 0) return 'Không thể kết nối máy chủ. Vui lòng kiểm tra API và thử lại.';
    if (error.status === 409) return 'Email hoặc số điện thoại này đã được sử dụng.';
    if (error.status === 503) return 'Dịch vụ gửi mã xác minh chưa sẵn sàng. Vui lòng thử lại sau.';

    const problem = error.error as ProblemDetails | null;
    return problem?.detail ?? problem?.title ?? 'Chưa thể tạo tài khoản. Vui lòng thử lại.';
  }
}
