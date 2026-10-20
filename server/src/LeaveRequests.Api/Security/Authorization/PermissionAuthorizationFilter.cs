using LeaveRequests.Api.Common;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LeaveRequests.Api.Security.Authorization;

/// <summary>
/// Authorization filter behind <see cref="HasPermissionAttribute"/>.
/// Returns <c>401</c> when nobody is signed in, and <c>403</c> (problem details
/// naming the missing permission) when the signed-in user lacks a permission.
/// </summary>
public sealed class PermissionAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUser _currentUser;
    private readonly string[] _requiredPermissions;

    public PermissionAuthorizationFilter(
        IPermissionService permissionService,
        ICurrentUser currentUser,
        string[] requiredPermissions)
    {
        _permissionService = permissionService;
        _currentUser = currentUser;
        _requiredPermissions = requiredPermissions;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
        {
            context.Result = Problems.Unauthorized(
                context.HttpContext,
                "Authentication is required to access this resource.");
            return;
        }

        var effective = await _permissionService.GetPermissionsAsync(
            userId, context.HttpContext.RequestAborted);

        var missing = _requiredPermissions
            .Where(permission => !effective.Contains(permission))
            .ToArray();

        if (missing.Length == 0)
        {
            return;
        }

        context.Result = Problems.Forbidden(
            context.HttpContext,
            $"The following permission(s) are required: {string.Join(", ", missing)}.",
            new Dictionary<string, object?> { ["missingPermissions"] = missing });
    }
}
