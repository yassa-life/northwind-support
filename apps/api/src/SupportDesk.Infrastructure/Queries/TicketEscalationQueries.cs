using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

public sealed class TicketEscalationQueries(SupportDbContext db) : ITicketEscalationQueries
{
    public async Task<IReadOnlyList<TicketEscalationDto>> GetForTicketAsync(int ticketId, CancellationToken ct)
    {
        var escalations = db.Set<TicketEscalation>()
            .AsNoTracking()
            .Where(escalation => escalation.TicketId == ticketId)
            .OrderByDescending(escalation => escalation.EscalatedAtUtc)
            .ThenByDescending(escalation => escalation.Id);

        return await Project(escalations)
            .ToListAsync(ct);
    }

    public Task<TicketEscalationDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var escalations = db.Set<TicketEscalation>()
            .AsNoTracking()
            .Where(escalation => escalation.Id == id);

        return Project(escalations).FirstOrDefaultAsync(ct);
    }

    private IQueryable<TicketEscalationDto> Project(IQueryable<TicketEscalation> escalations) =>
        from escalation in escalations
        from fromAgent in db.Set<Agent>().Where(x => x.Id == escalation.FromAgentId).DefaultIfEmpty()
        from toAgent in db.Set<Agent>().Where(x => x.Id == escalation.ToAgentId).DefaultIfEmpty()
        select new TicketEscalationDto(
            escalation.Id,
            escalation.TicketId,
            escalation.FromPriority,
            escalation.ToPriority,
            fromAgent == null ? null : new AgentSummaryDto(fromAgent.Id, fromAgent.FullName),
            toAgent == null ? null : new AgentSummaryDto(toAgent.Id, toAgent.FullName),
            escalation.FromDueAtUtc,
            escalation.ToDueAtUtc,
            escalation.Reason,
            escalation.EscalatedBy,
            escalation.EscalatedAtUtc);
}
