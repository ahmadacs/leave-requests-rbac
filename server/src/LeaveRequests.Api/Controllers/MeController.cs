using LeaveRequests.Api.Common;
using LeaveRequests.Api.Contracts.Auth;
using LeaveRequests.Api.Domain.Entities;
using LeaveRequests.Api.Security;
using LeaveRequests.Api.Security.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRequests.Api.Controllers;

[ApiController]
[Route("api/me")]
[AuthenticatedOnly]
public sealed class MeController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUser _currentUser;

    public MeController(
        UserManager<ApplicationUser> userManager,
        IPermissionService permissionService,
        ICurrentUser currentUser)
    {
        _userManager = userManager;
        _permissionService = permissionService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Problems.Unauthorized(HttpContext, "Authentication is required.");
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            return Problems.Unauthorized(HttpContext, "Authentication is required.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await _permissionService.GetPermissionsAsync(userId, ct);

        return Ok(new MeResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            user.TenantId,
            user.DepartmentId,
            roles.OrderBy(r => r).ToArray(),
            permissions.OrderBy(p => p, StringComparer.Ordinal).ToArray()));
    }
}
