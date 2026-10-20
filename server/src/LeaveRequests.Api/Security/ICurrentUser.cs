namespace LeaveRequests.Api.Security;

/// <summary>
/// The single place in the codebase that reads claims. Everything else
/// (controllers, domain services, DbContext filters) depends on this.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? TenantId { get; }
    Guid? DepartmentId { get; }
    string? Email { get; }
    string? Name { get; }
}
