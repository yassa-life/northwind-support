export interface AuthenticatedAgent {
  id: number;
  fullName: string;
  email: string;
}

export interface LoginResponse {
  token: string;
  expiresAtUtc: string;
  agent: AuthenticatedAgent;
}

export type AuthSession = LoginResponse;
