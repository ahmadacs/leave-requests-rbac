using System.Security.Claims;

namespace LeaveRequests.Api.Security;

/// <summary>
/// Reads the current user from the incoming request's claims principal.
/// Returns <c>null</c> values (rather than throwing) when there is no
/// HTTP context, which keeps background work and seeding safe.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId => ReadGuid(ClaimNames.Subject)
        ?? ReadGuid(ClaimTypes.NameIdentifier);

    public Guid? TenantId => ReadGuid(ClaimNames.TenantId);

    public Guid? DepartmentId => ReadGuid(ClaimNames.DepartmentId);

    public string? Email => Principal?.FindFirst(ClaimNames.Email)?.Value
        ?? Principal?.FindFirst(ClaimTypes.Email)?.Value;

    public string? Name => Principal?.FindFirst(ClaimNames.Name)?.Value
        ?? Principal?.FindFirst(ClaimTypes.Name)?.Value;

    private Guid? ReadGuid(string claimType)
    {
        var value = Principal?.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
