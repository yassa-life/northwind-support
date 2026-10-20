import { useCallback, useEffect, useMemo, useState } from 'react';
import { Outlet } from 'react-router-dom';
import { request } from '../../api/client';
import { AuthContext } from './AuthContext';
import { clearSession, loadSession, saveSession, unauthorizedEventName } from './session';
import type { AuthSession, LoginResponse } from './types';

export function AuthProvider() {
  const [session, setSession] = useState<AuthSession | null>(() => loadSession());

  useEffect(() => {
    const handleUnauthorized = () => setSession(null);
    window.addEventListener(unauthorizedEventName, handleUnauthorized);
    return () => window.removeEventListener(unauthorizedEventName, handleUnauthorized);
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    const response = await request<LoginResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    });
    saveSession(response);
    setSession(response);
  }, []);

  const logout = useCallback(() => {
    clearSession();
    setSession(null);
  }, []);

  const value = useMemo(() => ({ session, login, logout }), [session, login, logout]);

  return (
    <AuthContext.Provider value={value}>
      <Outlet />
    </AuthContext.Provider>
  );
}
