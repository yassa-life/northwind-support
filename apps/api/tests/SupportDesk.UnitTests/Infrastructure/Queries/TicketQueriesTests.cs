using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class TicketQueriesTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    private SupportDbContext _db = null!;
    private Customer _contoso = null!;
    private Customer _fabrikam = null!;
    private Category _general = null!;
    private Category _billing = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new SupportDbContext(options);

        await _db.Database.EnsureCreatedAsync();
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByExactPriority()
    {
        var query = new TicketQuery
        {
            Priority = TicketPriority.High,
            Page = 1,
            PageSize = 20
        };

        var result = await CreateQueries().GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, ticket => Assert.Equal(TicketPriority.High, ticket.Priority));
    }

    [Fact]
    public async Task GetPagedAsync_CombinesFiltersWithAnd()
    {
        var query = new TicketQuery
        {
            Priority = TicketPriority.High,
            CustomerId = _fabrikam.Id,
            CategoryId = _billing.Id,
            Page = 1,
            PageSize = 20
        };

        var result = await CreateQueries().GetPagedAsync(query, CancellationToken.None);

        var ticket = Assert.Single(result.Items);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0002", ticket.Reference);
        Assert.Equal(_fabrikam.Id, ticket.Customer.Id);
        Assert.Equal(_billing.Id, ticket.Category.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchesByCustomerName()
    {
        var query = new TicketQuery
        {
            Search = "  Contoso  ",
            Page = 1,
            PageSize = 20
        };

        var result = await CreateQueries().GetPagedAsync(query, CancellationToken.None);

        var ticket = Assert.Single(result.Items);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0001", ticket.Reference);
        Assert.Equal("Contoso", ticket.Customer.Name);
    }

    private TicketQueries CreateQueries() => new(
        _db,
        new FixedClock(),
        new SupportDesk.Application.Features.Tickets.Triage.SlaPolicy(
            SupportDesk.UnitTests.Application.Features.Tickets.Commands.TicketCommandTestContext.TestSlaOptions()));

    private async Task SeedAsync()
    {
        _contoso = new Customer(
            "Contoso",
            "support@contoso.test",
            null,
            CustomerTier.Premium,
            FixedClock.DefaultNow);

        _fabrikam = new Customer(
            "Fabrikam",
            "support@fabrikam.test",
            null,
            CustomerTier.Standard,
            FixedClock.DefaultNow);

        _general = new Category(
            "General",
            requiresSpecialist: false,
            forcesCriticalPriority: false);

        _billing = new Category(
            "Billing",
            requiresSpecialist: true,
            forcesCriticalPriority: false);

        _db.AddRange(_contoso, _fabrikam, _general, _billing);
        await _db.SaveChangesAsync();

        var tickets = new[]
        {
            Ticket.Raise(
                "TCK-0001",
                "Contoso email problem",
                "The customer cannot send email.",
                _contoso.Id,
                _general.Id,
                TicketPriority.High,
                FixedClock.DefaultNow),
            Ticket.Raise(
                "TCK-0002",
                "Incorrect invoice",
                "The invoice contains an incorrect charge.",
                _fabrikam.Id,
                _billing.Id,
                TicketPriority.High,
                FixedClock.DefaultNow),
            Ticket.Raise(
                "TCK-0003",
                "Printer paper request",
                "The customer needs more printer paper.",
                _fabrikam.Id,
                _general.Id,
                TicketPriority.Low,
                FixedClock.DefaultNow)
        };

        _db.AddRange(tickets);
        await _db.SaveChangesAsync();
    }
}
