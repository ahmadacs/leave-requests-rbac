namespace LeaveRequests.Api.Security;

/// <summary>
/// Names of the default roles seeded for every tenant. Only the Administrator
/// role is a system role: it cannot be edited or deleted and always holds every
/// permission.
/// </summary>
public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string HR = "HR";
    public const string Manager = "Manager";
    public const string Employee = "Employee";

    public static readonly IReadOnlyList<string> All =
        [Administrator, HR, Manager, Employee];
}
