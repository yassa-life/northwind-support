import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { TicketDetail } from '../../../types/api';
import { EscalationPanel } from './EscalationPanel';

const ticket: TicketDetail = {
  id: 1,
  reference: 'TCK-0001',
  title: 'Cannot sign in',
  description: 'The portal rejects every attempt.',
  status: 'Open',
  priority: 'High',
  customer: { id: 1, name: 'Contoso', email: 'support@contoso.test', phone: null, tier: 'Premium' },
  category: { id: 1, name: 'General', requiresSpecialist: false },
  assignedAgent: null,
  createdAtUtc: '2026-10-19T10:00:00Z',
  updatedAtUtc: '2026-10-19T10:00:00Z',
  dueAtUtc: '2026-10-19T14:00:00Z',
  resolvedAtUtc: null,
  slaStatus: 'WithinSla',
};

describe('EscalationPanel', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('blocks a short reason without calling the API', async () => {
    const escalate = vi.spyOn(ticketsApi, 'escalate');
    const user = userEvent.setup();
    render(<EscalationPanel ticket={ticket} onUpdated={vi.fn()} onEscalated={vi.fn()} />);

    await user.type(screen.getByLabelText('Reason'), 'bad');
    await user.click(screen.getByRole('button', { name: 'Escalate one level' }));

    expect(escalate).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent('between 5 and 500');
  });

  it('shows a 409 and leaves the ticket unchanged', async () => {
    vi.spyOn(ticketsApi, 'escalate').mockRejectedValue(new ApiError(409, 'Ticket is already Critical.'));
    const onUpdated = vi.fn();
    const user = userEvent.setup();
    render(<EscalationPanel ticket={ticket} onUpdated={onUpdated} onEscalated={vi.fn()} />);

    await user.type(screen.getByLabelText('Reason'), 'Customer impact increased');
    await user.click(screen.getByRole('button', { name: 'Escalate one level' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('already Critical');
    expect(onUpdated).not.toHaveBeenCalled();
  });
});
