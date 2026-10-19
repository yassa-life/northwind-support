import type { AuthSession } from './types';

const storageKey = 'northwind-support.session';

export const unauthorizedEventName = 'northwind-support:unauthorized';

export function loadSession(): AuthSession | null {
  const value = localStorage.getItem(storageKey);
  if (!value) {
    return null;
  }

  try {
    const session = JSON.parse(value) as AuthSession;
    if (!session.token || !session.agent || Date.parse(session.expiresAtUtc) <= Date.now()) {
      clearSession();
      return null;
    }

    return session;
  } catch {
    clearSession();
    return null;
  }
}

export function saveSession(session: AuthSession): void {
  localStorage.setItem(storageKey, JSON.stringify(session));
}

export function clearSession(): void {
  localStorage.removeItem(storageKey);
}
