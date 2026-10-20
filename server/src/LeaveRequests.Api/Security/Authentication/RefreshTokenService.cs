using System.Security.Cryptography;
using System.Text;
using LeaveRequests.Api.Configuration;
using LeaveRequests.Api.Data;
using LeaveRequests.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeaveRequests.Api.Security.Authentication;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly AppDbContext _db;
    private readonly JwtOptions _options;

    public RefreshTokenService(AppDbContext db, IOptions<JwtOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<string> IssueAsync(ApplicationUser user, string? ipAddress, CancellationToken ct = default)
    {
        var rawToken = GenerateToken();
        AddToken(user.Id, rawToken, ipAddress);
        await _db.SaveChangesAsync(ct);
        return rawToken;
    }

    public async Task<RefreshRotationResult> RotateAsync(string rawToken, string? ipAddress, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var existing = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null)
        {
            return new RefreshRotationResult(false, null, null, false);
        }

        // Reuse of an already-revoked token: assume theft, drop every session.
        if (existing.RevokedAtUtc is not null)
        {
            await RevokeAllForUserAsync(existing.UserId, ct);
            return new RefreshRotationResult(false, null, null, true);
        }

        if (existing.ExpiresAtUtc <= DateTime.UtcNow)
        {
            existing.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new RefreshRotationResult(false, null, null, false);
        }

        if (!existing.User.IsActive)
        {
            await RevokeAllForUserAsync(existing.UserId, ct);
            return new RefreshRotationResult(false, null, null, false);
        }

        var newRawToken = GenerateToken();
        existing.RevokedAtUtc = DateTime.UtcNow;
        existing.ReplacedByHash = Hash(newRawToken);
        AddToken(existing.UserId, newRawToken, ipAddress);
        await _db.SaveChangesAsync(ct);

        return new RefreshRotationResult(true, existing.User, newRawToken, false);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.RevokedAtUtc is not null)
        {
            return;
        }

        token.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(ct);

        if (active.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var token in active)
        {
            token.RevokedAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    private void AddToken(Guid userId, string rawToken, string? ipAddress)
    {
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(rawToken),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_options.RefreshTokenDays),
            CreatedByIp = ipAddress
        });
    }

    private static string GenerateToken() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
