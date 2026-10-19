import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './client';
import { loadSession, saveSession } from '../features/auth/session';

const session = {
  token: 'test-token',
  expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
  agent: {
    id: 1,
    fullName: 'Alex Turner',
    email: 'alex.turner@northwind-support.example',
  },
};

describe('API authentication', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('attaches the saved token to API requests', async () => {
    saveSession(session);
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await request<{ ok: boolean }>('/tickets');

    expect(fetch).toHaveBeenCalledWith(
      '/api/tickets',
      expect.objectContaining({
        headers: expect.objectContaining({ Authorization: 'Bearer test-token' }),
      }),
    );
  });

  it('clears the saved session when a protected request returns 401', async () => {
    saveSession(session);
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ title: 'Unauthorized' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await expect(request('/tickets')).rejects.toThrow();

    expect(loadSession()).toBeNull();
  });
});
