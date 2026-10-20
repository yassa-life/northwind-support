using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Triage;

public sealed class TriagePolicyTests
{
    private readonly SlaPolicy _sla = new(
        SupportDesk.UnitTests.Application.Features.Tickets.Commands.TicketCommandTestContext.TestSlaOptions());

    [Theory]
    [InlineData(TicketPriority.Low, CustomerTier.Standard, 72)]
    [InlineData(TicketPriority.Low, CustomerTier.Premium, 36)]
    [InlineData(TicketPriority.Medium, CustomerTier.Standard, 24)]
    [InlineData(TicketPriority.Medium, CustomerTier.Premium, 12)]
    [InlineData(TicketPriority.High, CustomerTier.Standard, 8)]
    [InlineData(TicketPriority.High, CustomerTier.Premium, 4)]
    [InlineData(TicketPriority.Critical, CustomerTier.Standard, 4)]
    [InlineData(TicketPriority.Critical, CustomerTier.Premium, 2)]
    public void Sla_window_uses_priority_and_customer_tier(
        TicketPriority priority,
        CustomerTier tier,
        double expectedHours) =>
        Assert.Equal(expectedHours, _sla.Decide(priority, tier).Window.TotalHours);

    [Fact]
    public void Sla_window_never_goes_below_floor()
    {
        var policy = new SlaPolicy(new SlaOptions
        {
            BaseWindowHours = new Dictionary<string, double> { ["Critical"] = 1 },
            PremiumCustomerMultiplier = 0.1,
            MinimumWindowHours = 1,
            AtRiskThresholdPercent = 25
        });

        Assert.Equal(1, policy.Decide(TicketPriority.Critical, CustomerTier.Premium).Window.TotalHours);
    }

    [Fact]
    public void Forced_category_overrides_requested_priority()
    {
        var category = new Category("Outage", true, true);
        Assert.Equal(TicketPriority.Critical, PriorityPolicy.Decide(TicketPriority.Low, category).Priority);
    }

    [Fact]
    public async Task Assignment_selects_first_candidate_and_keeps_current_when_eligible()
    {
        var repository = new Mock<IAgentRepository>();
        repository.Setup(x => x.GetAssignmentCandidatesAsync(It.IsAny<int>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AgentCandidate(2, "Least loaded", 1, 8),
                new AgentCandidate(5, "Current", 2, 8)
            ]);
        var policy = new AssignmentPolicy(repository.Object);
        var category = new Category("Billing", true, false);

        var selected = await policy.DecideAsync(category, null, CancellationToken.None);
        var kept = await policy.DecideAsync(category, 5, CancellationToken.None);

        Assert.Equal(2, selected.AgentId);
        Assert.Equal(5, kept.AgentId);
    }

    [Fact]
    public async Task Assignment_returns_normal_unassigned_decision_when_nobody_is_eligible()
    {
        var repository = new Mock<IAgentRepository>();
        repository.Setup(x => x.GetAssignmentCandidatesAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var decision = await new AssignmentPolicy(repository.Object)
            .DecideAsync(new Category("Security", true, true), null, CancellationToken.None);

        Assert.Null(decision.AgentId);
        Assert.Contains("left unassigned", decision.Reason);
    }
}
