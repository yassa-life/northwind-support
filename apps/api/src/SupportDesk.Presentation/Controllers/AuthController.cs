using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;
using SupportDesk.Presentation.Auth;

namespace SupportDesk.Presentation.Controllers;

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public sealed record AuthenticatedAgentDto(int Id, string FullName, string Email);

public sealed record LoginResponse(
    string Token,
    DateTime ExpiresAtUtc,
    AuthenticatedAgentDto Agent);

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IAgentRepository agents,
    IPasswordHasher<Agent> passwordHasher,
    IJwtTokenService tokens) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var agent = await agents.GetByEmailAsync(request.Email, ct);

        if (agent is null || !agent.IsActive || string.IsNullOrWhiteSpace(agent.PasswordHash))
        {
            return InvalidCredentials();
        }

        var result = passwordHasher.VerifyHashedPassword(agent, agent.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return InvalidCredentials();
        }

        var token = tokens.IssueFor(agent);
        return Ok(new LoginResponse(
            token.Value,
            token.ExpiresAtUtc,
            new AuthenticatedAgentDto(agent.Id, agent.FullName, agent.Email)));
    }

    [HttpGet("me")]
    [ProducesResponseType<AuthenticatedAgentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<AuthenticatedAgentDto> Me()
    {
        var idValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var fullName = User.FindFirstValue(ClaimTypes.Name);
        var email = User.FindFirstValue(ClaimTypes.Email);

        if (!int.TryParse(idValue, out var id) || fullName is null || email is null)
        {
            return Unauthorized();
        }

        return Ok(new AuthenticatedAgentDto(id, fullName, email));
    }

    private UnauthorizedObjectResult InvalidCredentials() =>
        Unauthorized(new ProblemDetails
        {
            Title = "Unauthorized",
            Detail = "The email or password is incorrect.",
            Status = StatusCodes.Status401Unauthorized
        });
}
