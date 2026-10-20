namespace LeaveRequests.Api.Security;

/// <summary>
/// Names of the JWT claims issued by this API. Centralised so the token
/// service and <c>ICurrentUser</c> never disagree on spelling.
/// </summary>
public static class ClaimNames
{
    public const string Subject = "sub";
    public const string TenantId = "tenant_id";
    public const string DepartmentId = "department_id";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
}
