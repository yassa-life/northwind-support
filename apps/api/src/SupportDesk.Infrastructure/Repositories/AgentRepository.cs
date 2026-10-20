using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Repositories;

public sealed class AgentRepository(SupportDbContext db) : IAgentRepository
{
    /// <summary>Loads the whole aggregate, specializations included.</summary>
    public Task<Agent?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Set<Agent>()
            .Include(a => a.Specializations)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Agent?> GetByEmailAsync(string email, CancellationToken ct) =>
        db.Set<Agent>().FirstOrDefaultAsync(a => a.Email == email.Trim(), ct);

    public async Task<IReadOnlyList<AgentCandidate>> GetAssignmentCandidatesAsync(
        int categoryId,
        bool requiresSpecialist,
        CancellationToken ct)
    {
        var agents = db.Set<Agent>().AsNoTracking().Where(agent => agent.IsActive);

        if (requiresSpecialist)
        {
            agents = agents.Where(agent => agent.Specializations.Any(x => x.CategoryId == categoryId));
        }

        var candidates = await agents
            .Select(agent => new
            {
                agent.Id,
                agent.FullName,
                OpenTicketCount = db.Set<Ticket>().Count(ticket =>
                    ticket.AssignedAgentId == agent.Id &&
                    ticket.Status != TicketStatus.Resolved &&
                    ticket.Status != TicketStatus.Closed),
                agent.MaxOpenTickets
            })
            .Where(agent => agent.OpenTicketCount < agent.MaxOpenTickets)
            .OrderBy(agent => agent.OpenTicketCount)
            .ThenBy(agent => agent.Id)
            .ToListAsync(ct);

        return candidates
            .Select(agent => new AgentCandidate(
                agent.Id,
                agent.FullName,
                agent.OpenTicketCount,
                agent.MaxOpenTickets))
            .ToList();
    }
}
