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

export interface AuthenticatedUser {
  id: string;
  fullName: string;
  email: string;
  phoneNumber: string;
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
