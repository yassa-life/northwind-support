using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    AssignmentPolicy assignment,
    SlaPolicy sla,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ITicketEscalationQueries escalationQueries)
{
    public async Task<EscalationResultDto> HandleAsync(
        int id,
        EscalateTicketRequest request,
        string escalatedBy,
        CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ticket", id);
        var customer = await customers.GetByIdAsync(ticket.CustomerId, ct)
            ?? throw new NotFoundException("Customer", ticket.CustomerId);
        var category = await categories.GetByIdAsync(ticket.CategoryId, ct)
            ?? throw new NotFoundException("Category", ticket.CategoryId);
        var nextPriority = ticket.GetEscalatedPriority();
        var now = clock.UtcNow;
        var slaDecision = sla.Decide(nextPriority, customer.Tier);
        var assignmentDecision = await assignment.DecideAsync(category, ticket.AssignedAgentId, ct);

        var history = ticket.Escalate(
            nextPriority,
            now.Add(slaDecision.Window),
            slaDecision.WindowMinutes,
            assignmentDecision.AgentId,
            request.Reason,
            escalatedBy,
            now);
        await tickets.AddEscalationAsync(history, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new EscalationResultDto(
            await getTicket.HandleAsync(id, ct),
            await escalationQueries.GetByIdAsync(history.Id, ct)
                ?? throw new InvalidOperationException("Saved escalation could not be reloaded."));
    }
}
