using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class TicketEscalationTests
{
    [Fact]
    public void Escalate_raises_one_level_and_records_before_and_after_values()
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.Medium).AssignedTo(2).Build();
        ticket.ApplyTriage(TicketPriority.Medium, FixedClock.DefaultNow.AddHours(12), 720, 2, FixedClock.DefaultNow);
        var later = FixedClock.DefaultNow.AddHours(1);

        var history = ticket.Escalate(
            TicketPriority.High,
            later.AddHours(4),
            240,
            3,
            "Customer impact increased",
            "lead@example.com",
            later);

        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(3, ticket.AssignedAgentId);
        Assert.Equal(later.AddHours(4), ticket.DueAtUtc);
        Assert.Equal(TicketPriority.Medium, history.FromPriority);
        Assert.Equal(TicketPriority.High, history.ToPriority);
        Assert.Equal(2, history.FromAgentId);
        Assert.Equal(3, history.ToAgentId);
        Assert.Equal("lead@example.com", history.EscalatedBy);
    }

    [Fact]
    public void Escalate_rejects_critical_ticket()
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.Critical).Build();
        Assert.Throws<BusinessRuleViolationException>(() => ticket.Escalate(
            TicketPriority.Critical, FixedClock.DefaultNow, 60, null, "Valid reason", "lead", FixedClock.DefaultNow));
    }

    [Fact]
    public void Escalate_rejects_skipping_a_priority_level()
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.Low).Build();
        Assert.Throws<BusinessRuleViolationException>(() => ticket.Escalate(
            TicketPriority.High, FixedClock.DefaultNow, 60, null, "Valid reason", "lead", FixedClock.DefaultNow));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void Escalate_rejects_terminal_ticket(TicketStatus status)
    {
        var ticket = new TicketBuilder().WithStatus(status).Build();
        Assert.Throws<BusinessRuleViolationException>(() => ticket.Escalate(
            TicketPriority.High, FixedClock.DefaultNow, 60, null, "Valid reason", "lead", FixedClock.DefaultNow));
    }
}
