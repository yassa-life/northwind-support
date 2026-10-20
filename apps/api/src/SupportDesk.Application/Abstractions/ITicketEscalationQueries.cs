using SupportDesk.Application.Contracts.Tickets;

namespace SupportDesk.Application.Abstractions;

public interface ITicketEscalationQueries
{
    Task<IReadOnlyList<TicketEscalationDto>> GetForTicketAsync(int ticketId, CancellationToken ct);
    Task<TicketEscalationDto?> GetByIdAsync(int id, CancellationToken ct);
}
