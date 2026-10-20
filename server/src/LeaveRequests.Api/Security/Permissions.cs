namespace LeaveRequests.Api.Security;

/// <summary>
/// Static catalogue of every permission in the system. The database is synced
/// from this list at startup (see <c>DbSeeder</c>), so new codes are inserted
/// automatically. Names follow the <c>resource.action</c> convention.
/// </summary>
public static class Permissions
{
    public const string LeavesGroup = "Leaves";
    public const string UsersGroup = "Users";
    public const string RolesGroup = "Roles";
    public const string AuditGroup = "Audit";

    public static class Leaves
    {
        public const string ViewOwn = "leaves.view.own";
        public const string Create = "leaves.create";
        public const string Cancel = "leaves.cancel";
        public const string ViewTeam = "leaves.view.team";
        public const string Approve = "leaves.approve";
        public const string ApproveLong = "leaves.approve.long";
        public const string ViewAll = "leaves.view.all";
        public const string Export = "leaves.export";
    }

    public static class Users
    {
        public const string View = "users.view";
        public const string Manage = "users.manage";
    }

    public static class Roles
    {
        public const string View = "roles.view";
        public const string Manage = "roles.manage";
    }

    public static class Audit
    {
        public const string View = "audit.view";
    }

    public sealed record Definition(string Code, string Group, string Description);

    public static readonly IReadOnlyList<Definition> All =
    [
        new(Leaves.ViewOwn, LeavesGroup, "See your own leave requests"),
        new(Leaves.Create, LeavesGroup, "Create a leave request for yourself"),
        new(Leaves.Cancel, LeavesGroup, "Cancel your own pending request"),
        new(Leaves.ViewTeam, LeavesGroup, "See the requests of your department"),
        new(Leaves.Approve, LeavesGroup, "Approve or reject requests (department rule applies)"),
        new(Leaves.ApproveLong, LeavesGroup, "Approve requests longer than 10 calendar days"),
        new(Leaves.ViewAll, LeavesGroup, "See every request in the company"),
        new(Leaves.Export, LeavesGroup, "Export requests to CSV"),
        new(Users.View, UsersGroup, "See users"),
        new(Users.Manage, UsersGroup, "Create users, assign roles, activate/deactivate"),
        new(Roles.View, RolesGroup, "See roles"),
        new(Roles.Manage, RolesGroup, "Create, edit and delete roles"),
        new(Audit.View, AuditGroup, "See the role and assignment audit log"),
    ];
}
