using LeaveRequests.Api.Domain.Entities;

namespace LeaveRequests.Api.Security.Authentication;

public sealed record RefreshRotationResult(
    bool Succeeded,
    ApplicationUser? User,
    string? RawToken,
    bool ReuseDetected);

public interface IRefreshTokenService
{
    /// <summary>Creates a new refresh token and returns the raw (unhashed) value.</summary>
    Task<string> IssueAsync(ApplicationUser user, string? ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Validates and rotates a raw refresh token. Reusing an already-revoked
    /// token revokes every active session of that user.
    /// </summary>
    Task<RefreshRotationResult> RotateAsync(string rawToken, string? ipAddress, CancellationToken ct = default);

    /// <summary>Revokes a single raw refresh token (used by logout).</summary>
    Task RevokeAsync(string rawToken, CancellationToken ct = default);

    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}
