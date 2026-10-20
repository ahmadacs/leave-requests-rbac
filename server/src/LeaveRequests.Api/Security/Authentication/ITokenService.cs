using LeaveRequests.Api.Domain.Entities;

namespace LeaveRequests.Api.Security.Authentication;

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);

public interface ITokenService
{
    /// <summary>
    /// Issues a short-lived JWT access token carrying <c>sub</c>,
    /// <c>tenant_id</c>, <c>department_id</c>, <c>role</c>, <c>email</c> and
    /// <c>name</c>. Permissions are deliberately NOT included.
    /// </summary>
    AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);
}
