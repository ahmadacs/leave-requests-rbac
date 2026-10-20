namespace LeaveRequests.Api.Audit;

public static class AuditActions
{
    public const string RoleCreated = "role.created";
    public const string RoleUpdated = "role.updated";
    public const string RoleDeleted = "role.deleted";
    public const string UserCreated = "user.created";
    public const string UserRolesUpdated = "user.roles.updated";
    public const string UserDeactivated = "user.deactivated";
    public const string UserActivated = "user.activated";
}
