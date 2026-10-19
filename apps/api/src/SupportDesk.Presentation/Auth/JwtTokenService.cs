using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Presentation.Auth;

public sealed record IssuedToken(string Value, DateTime ExpiresAtUtc);

public interface IJwtTokenService
{
    IssuedToken IssueFor(Agent agent);
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public IssuedToken IssueFor(Agent agent)
    {
        var issuedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = issuedAtUtc.AddMinutes(_options.LifetimeMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, agent.Id.ToString()),
            new(ClaimTypes.Name, agent.FullName),
            new(ClaimTypes.Email, agent.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ];

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            issuedAtUtc,
            expiresAtUtc,
            credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
