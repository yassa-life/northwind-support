using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Repositories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Repositories;

public sealed class AgentRepositoryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private SupportDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new SupportDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Candidates_are_active_specialists_below_capacity_and_deterministically_ordered()
    {
        var customer = new Customer("Customer", "customer@example.test", null, CustomerTier.Standard, FixedClock.DefaultNow);
        var billing = new Category("Billing", requiresSpecialist: true, forcesCriticalPriority: false);
        _db.AddRange(customer, billing);
        await _db.SaveChangesAsync();

        var first = Agent("First", 3, billing.Id);
        var second = Agent("Second", 3, billing.Id);
        var inactive = Agent("Inactive", 3, billing.Id);
        inactive.Deactivate();
        var nonSpecialist = Agent("Non-specialist", 3);
        var atCapacity = Agent("At capacity", 1, billing.Id);
        _db.AddRange(first, second, inactive, nonSpecialist, atCapacity);
        await _db.SaveChangesAsync();

        _db.AddRange(
            AssignedTicket("TCK-1001", customer.Id, billing.Id, first.Id),
            AssignedTicket("TCK-1002", customer.Id, billing.Id, second.Id),
            AssignedTicket("TCK-1003", customer.Id, billing.Id, atCapacity.Id));
        await _db.SaveChangesAsync();

        var candidates = await new AgentRepository(_db)
            .GetAssignmentCandidatesAsync(billing.Id, requiresSpecialist: true, CancellationToken.None);

        Assert.Collection(
            candidates,
            candidate => Assert.Equal(first.Id, candidate.Id),
            candidate => Assert.Equal(second.Id, candidate.Id));
        Assert.All(candidates, candidate => Assert.Equal(1, candidate.OpenTicketCount));
    }

    private static Agent Agent(string name, int capacity, params int[] specializations)
    {
        var agent = new Agent(name, $"{name.ToLowerInvariant()}@example.test", capacity, FixedClock.DefaultNow);
        foreach (var categoryId in specializations)
        {
            agent.AddSpecialization(categoryId);
        }

        return agent;
    }

    private static Ticket AssignedTicket(string reference, int customerId, int categoryId, int agentId)
    {
        var ticket = Ticket.Raise(
            reference,
            "Assignment test ticket",
            "Used to calculate the open-ticket count.",
            customerId,
            categoryId,
            TicketPriority.Medium,
            FixedClock.DefaultNow);
        ticket.ApplyTriage(
            TicketPriority.Medium,
            FixedClock.DefaultNow.AddHours(24),
            1440,
            agentId,
            FixedClock.DefaultNow);
        return ticket;
    }
}
