using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.Application.Features.Tickets.Triage;

namespace SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;

/// <summary>
/// Raises a ticket for a customer.
/// </summary>
public sealed class RaiseTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    AssignmentPolicy assignment,
    SlaPolicy sla,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<RaiseTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The customer or the category does not exist.</exception>
    public async Task<TicketDetailDto> HandleAsync(CreateTicketRequest request, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(request.CustomerId, ct)
            ?? throw new NotFoundException("Customer", request.CustomerId);
        var category = await categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw new NotFoundException("Category", request.CategoryId);
        var priority = PriorityPolicy.Decide(request.RequestedPriority, category);
        var slaDecision = sla.Decide(priority.Priority, customer.Tier);
        var assignmentDecision = await assignment.DecideAsync(category, null, ct);
        var now = clock.UtcNow;

        var ticket = Ticket.Raise(
            await tickets.NextReferenceAsync(ct),
            request.Title,
            request.Description,
            request.CustomerId,
            request.CategoryId,
            priority.Priority,
            now);
        ticket.ApplyTriage(
            priority.Priority,
            now.Add(slaDecision.Window),
            slaDecision.WindowMinutes,
            assignmentDecision.AgentId,
            now);

        await tickets.AddAsync(ticket, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} raised for customer {CustomerId} in category {CategoryId}.",
            ticket.Reference, ticket.CustomerId, ticket.CategoryId);

        var result = await getTicket.HandleAsync(ticket.Id, ct);
        result.Triage = new TriageDecisionDto(
            priority.Reason,
            slaDecision.Reason,
            assignmentDecision.Reason);
        return result;
    }
}
