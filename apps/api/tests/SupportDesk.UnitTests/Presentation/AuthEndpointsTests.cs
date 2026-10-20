using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;
using SupportDesk.Presentation.Controllers;

namespace SupportDesk.UnitTests.Presentation;

public sealed class AuthEndpointsTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public AuthEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Wrong_password_returns_401_without_a_token()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(AuthApiFactory.ActiveEmail, "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The email or password is incorrect.", body);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_return_the_same_401_body()
    {
        using var client = _factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(AuthApiFactory.ActiveEmail, "wrong-password"));
        var unknownEmail = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("unknown@northwind-support.example", "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknownEmail.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inactive_agent_cannot_log_in()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(AuthApiFactory.InactiveEmail, AuthApiFactory.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_returns_401_without_a_token()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_login_token_can_open_the_current_user_endpoint()
    {
        using var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(AuthApiFactory.ActiveEmail, AuthApiFactory.Password));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.NotNull(login);
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        var meResponse = await client.GetAsync("/api/auth/me");
        meResponse.EnsureSuccessStatusCode();
        var me = await meResponse.Content.ReadFromJsonAsync<AuthenticatedAgentDto>();

        Assert.NotNull(me);
        Assert.Equal(AuthApiFactory.ActiveEmail, me.Email);
        Assert.Equal("Alex Turner", me.FullName);
    }
}

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public const string ActiveEmail = "alex.turner@northwind-support.example";
    public const string InactiveEmail = "inactive@northwind-support.example";
    public const string Password = "LocalAgent123!";

    private readonly IAgentRepository _agents;

    public AuthApiFactory()
    {
        var passwordHasher = new PasswordHasher<Agent>();
        var activeAgent = new Agent("Alex Turner", ActiveEmail, 10, DateTime.UtcNow);
        activeAgent.SetPasswordHash(passwordHasher.HashPassword(activeAgent, Password));

        var inactiveAgent = new Agent("Inactive Agent", InactiveEmail, 10, DateTime.UtcNow);
        inactiveAgent.SetPasswordHash(passwordHasher.HashPassword(inactiveAgent, Password));
        inactiveAgent.Deactivate();

        var agents = new Mock<IAgentRepository>();
        agents.Setup(repository => repository.GetByEmailAsync(ActiveEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeAgent);
        agents.Setup(repository => repository.GetByEmailAsync(InactiveEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveAgent);
        agents.Setup(repository => repository.GetByEmailAsync(
                It.Is<string>(email => email != ActiveEmail && email != InactiveEmail),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);
        _agents = agents.Object;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Server=localhost;Database=SupportDeskTests;User Id=sa;Password=unused;TrustServerCertificate=True");
        builder.UseSetting("Jwt:SigningKey", "TEST-ONLY-SIGNING-KEY-THAT-IS-LONG-ENOUGH");
        builder.UseSetting("Jwt:Issuer", "SupportDesk.Tests");
        builder.UseSetting("Jwt:Audience", "SupportDesk.Tests.Client");
        builder.UseSetting("Jwt:LifetimeMinutes", "15");
        builder.UseSetting("Sla:BaseWindowHours:Low", "72");
        builder.UseSetting("Sla:BaseWindowHours:Medium", "24");
        builder.UseSetting("Sla:BaseWindowHours:High", "8");
        builder.UseSetting("Sla:BaseWindowHours:Critical", "4");
        builder.UseSetting("Sla:PremiumCustomerMultiplier", "0.5");
        builder.UseSetting("Sla:MinimumWindowHours", "1");
        builder.UseSetting("Sla:AtRiskThresholdPercent", "25");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentRepository>();
            services.AddSingleton(_agents);
        });
    }
}
