using LeaveRequests.Api.Common;

namespace LeaveRequests.Api.Security.Authorization;

/// <summary>
/// Prevents privilege escalation: nobody may grant a permission they do not
/// themselves hold. Used when creating/editing roles and when assigning roles
/// to users.
/// </summary>
public sealed class PrivilegeGuard
{
    private readonly IPermissionService _permissionService;

    public PrivilegeGuard(IPermissionService permissionService) => _permissionService = permissionService;

    public async Task<Result> EnsureCanGrantAsync(
        Guid callerId,
        IEnumerable<string> permissionCodes,
        CancellationToken ct = default)
    {
        var mine = await _permissionService.GetPermissionsAsync(callerId, ct);

        var missing = permissionCodes
            .Distinct(StringComparer.Ordinal)
            .Where(code => !mine.Contains(code))
            .ToArray();

        return missing.Length == 0
            ? Result.Success()
            : Result.Forbidden(
                $"You cannot grant permission(s) you do not hold: {string.Join(", ", missing)}.");
    }
}
