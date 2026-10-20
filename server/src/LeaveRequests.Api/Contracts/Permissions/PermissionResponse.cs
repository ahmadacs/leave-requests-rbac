namespace LeaveRequests.Api.Contracts.Permissions;

public sealed record PermissionResponse(string Code, string Group, string Description)
{
    public static PermissionResponse From(Domain.Entities.Permission permission) =>
        new(permission.Code, permission.Group, permission.Description);
}
