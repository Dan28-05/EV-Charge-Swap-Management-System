export interface LoginCredentials {
  identifier: string;
  password: string;
}

export interface RegisterCustomerRequest {
  fullName: string;
  email: string;
  phoneNumber: string;
  password: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface ResetPasswordRequest {
  identifier: string;
  code: string;
  newPassword: string;
}

export interface AuthenticatedUser {
  id: string;
  fullName: string;
  email: string;
  phoneNumber: string | null;
  emailConfirmed: boolean;
  phoneNumberConfirmed: boolean;
  roles: string[];
}

export interface AuthenticationResponse {
  tokenType: string;
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
  user: AuthenticatedUser;
}

export interface ProblemDetails {
  detail?: string;
  title?: string;
}
