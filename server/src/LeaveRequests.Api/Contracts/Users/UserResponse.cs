using LeaveRequests.Api.Domain.Entities;

namespace LeaveRequests.Api.Contracts.Users;

public sealed record UserResponse(
    Guid Id,
    string Email,
    string Name,
    Guid TenantId,
    Guid? DepartmentId,
    bool IsActive,
    IReadOnlyCollection<string> Roles)
{
    public static UserResponse From(ApplicationUser user, IEnumerable<string> roles) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            user.TenantId,
            user.DepartmentId,
            user.IsActive,
            roles.OrderBy(r => r).ToArray());
}
