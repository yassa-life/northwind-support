import { formatDateTime } from '../../../lib/format';
import type { TicketEscalation } from '../../../types/api';

export function EscalationHistory({ items }: { items: TicketEscalation[] }) {
  return (
    <div className="card">
      <h2>Escalation history</h2>
      {items.length === 0 ? (
        <p>No escalations recorded.</p>
      ) : (
        <ol className="history-list">
          {items.map((item) => (
            <li key={item.id}>
              <strong>{item.fromPriority} → {item.toPriority}</strong>
              <span>{item.fromAgent?.fullName ?? 'Unassigned'} → {item.toAgent?.fullName ?? 'Unassigned'}</span>
              <span>Due {formatDateTime(item.fromDueAtUtc)} → {formatDateTime(item.toDueAtUtc)}</span>
              <p>{item.reason}</p>
              <small>{item.escalatedBy} · {formatDateTime(item.escalatedAtUtc)}</small>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
