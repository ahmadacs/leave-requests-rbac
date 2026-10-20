namespace LeaveRequests.Api.Contracts.Roles;

public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    IReadOnlyCollection<string> Permissions);
