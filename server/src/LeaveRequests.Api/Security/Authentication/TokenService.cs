using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LeaveRequests.Api.Configuration;
using LeaveRequests.Api.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LeaveRequests.Api.Security.Authentication;

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;

    public TokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(ClaimNames.Subject, user.Id.ToString()),
            new(ClaimNames.TenantId, user.TenantId.ToString()),
            new(ClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (user.DepartmentId is { } departmentId)
        {
            claims.Add(new Claim(ClaimNames.DepartmentId, departmentId.ToString()));
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            claims.Add(new Claim(ClaimNames.Role, role));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: credentials);

        var value = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(value, expiresAt);
    }
}
