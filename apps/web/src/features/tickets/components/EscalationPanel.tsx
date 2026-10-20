import { type FormEvent, useState } from 'react';
import { ticketsApi } from '../../../api/tickets';
import { toErrorMessage } from '../../../api/client';
import type { TicketDetail } from '../../../types/api';
import { Field } from '../../../components/Field';

interface Props {
  ticket: TicketDetail;
  onUpdated: (ticket: TicketDetail) => void;
  onEscalated: () => Promise<void>;
}

export function EscalationPanel({ ticket, onUpdated, onEscalated }: Props) {
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string>();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const cannotEscalate = ticket.priority === 'Critical' || ['Resolved', 'Closed'].includes(ticket.status);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const trimmed = reason.trim();
    if (trimmed.length < 5 || trimmed.length > 500) {
      setError('Reason must be between 5 and 500 characters.');
      return;
    }

    setError(undefined);
    setIsSubmitting(true);
    try {
      const result = await ticketsApi.escalate(ticket.id, trimmed);
      onUpdated(result.ticket);
      setReason('');
      await onEscalated();
    } catch (caught) {
      setError(toErrorMessage(caught, 'Unable to escalate this ticket.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="card">
      <h2>Escalate ticket</h2>
      {cannotEscalate ? (
        <p>This ticket cannot be escalated because it is Critical, Resolved, or Closed.</p>
      ) : (
        <form onSubmit={submit} noValidate>
          <Field id="escalation-reason" label="Reason" error={error} hint="5–500 characters">
            {(props) => (
              <textarea {...props} rows={4} value={reason} onChange={(event) => setReason(event.target.value)} />
            )}
          </Field>
          <button className="button button--primary" disabled={isSubmitting}>
            {isSubmitting ? 'Escalating…' : 'Escalate one level'}
          </button>
        </form>
      )}
    </div>
  );
}
