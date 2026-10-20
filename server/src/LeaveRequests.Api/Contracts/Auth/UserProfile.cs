using LeaveRequests.Api.Domain.Entities;

namespace LeaveRequests.Api.Contracts.Auth;

public sealed record UserProfile(
    Guid Id,
    string Email,
    string Name,
    Guid TenantId,
    Guid? DepartmentId,
    IReadOnlyCollection<string> Roles)
{
    public static UserProfile From(ApplicationUser user, IEnumerable<string> roles) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            user.TenantId,
            user.DepartmentId,
            roles.ToArray());
}
