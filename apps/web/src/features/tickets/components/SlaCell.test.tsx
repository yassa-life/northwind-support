import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SlaStatus } from '../../../types/api';
import { SlaCell } from './SlaCell';

describe('SlaCell', () => {
  it.each<[SlaStatus, string]>([
    ['WithinSla', 'Within SLA'],
    ['AtRisk', 'At risk'],
    ['Breached', 'Breached'],
    ['Met', 'Met'],
    ['NotApplicable', 'No SLA'],
  ])('renders %s with a readable label', (status, label) => {
    render(<SlaCell dueAtUtc={null} slaStatus={status} />);
    expect(screen.getByText(label)).toBeInTheDocument();
  });
});
