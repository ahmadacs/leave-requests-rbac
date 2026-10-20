namespace LeaveRequests.Api.Contracts.Auth;

public sealed record MeResponse(
    Guid Id,
    string Email,
    string Name,
    Guid TenantId,
    Guid? DepartmentId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);
