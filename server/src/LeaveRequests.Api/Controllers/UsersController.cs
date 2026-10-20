using LeaveRequests.Api.Audit;
using LeaveRequests.Api.Common;
using LeaveRequests.Api.Contracts.Users;
using LeaveRequests.Api.Data;
using LeaveRequests.Api.Domain.Entities;
using LeaveRequests.Api.Realtime;
using LeaveRequests.Api.Security;
using LeaveRequests.Api.Security.Authentication;
using LeaveRequests.Api.Security.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveRequests.Api.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController : ApiControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _currentUser;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly PrivilegeGuard _guard;
    private readonly IUserNotifier _notifier;

    public UsersController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        IPermissionService permissions,
        ICurrentUser currentUser,
        IRefreshTokenService refreshTokens,
        PrivilegeGuard guard,
        IUserNotifier notifier)
    {
        _db = db;
        _userManager = userManager;
        _permissions = permissions;
        _currentUser = currentUser;
        _refreshTokens = refreshTokens;
        _guard = guard;
        _notifier = notifier;
    }

    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        var response = new List<UserResponse>(users.Count);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            response.Add(UserResponse.From(user, roles));
        }

        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Users.View)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct);
        if (user is null)
        {
            return Problems.NotFound(HttpContext, $"User '{id}' was not found.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(UserResponse.From(user, roles));
    }

    [HttpPost]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;
        var callerId = _currentUser.UserId ?? Guid.Empty;

        if (await _userManager.FindByEmailAsync(request.Email) is not null)
        {
            return Problems.Conflict(
                HttpContext,
                $"A user with the email '{request.Email}' already exists.");
        }

        if (request.DepartmentId is { } departmentId
            && !await _db.Departments.AnyAsync(d => d.Id == departmentId && d.TenantId == tenantId, ct))
        {
            return Problems.BadRequest(HttpContext, "The selected department does not exist in your company.");
        }

        var roles = await ResolveRolesAsync(tenantId, request.Roles ?? [], ct);
        if (roles is null)
        {
            return Problems.BadRequest(HttpContext, "One or more roles do not exist in your company.");
        }

        // Escalation guard: you can only assign roles whose permissions you hold.
        var grants = await PermissionsOfRolesAsync(roles.Select(r => r.Id), ct);
        var guard = await _guard.EnsureCanGrantAsync(callerId, grants, ct);
        if (!guard.IsSuccess)
        {
            return MapResult(guard);
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            FullName = request.FullName,
            TenantId = tenantId,
            DepartmentId = request.DepartmentId,
            IsActive = true,
            LockoutEnabled = true
        };

        var created = await _userManager.CreateAsync(user, request.Password ?? DbSeeder.DemoPassword);
        if (!created.Succeeded)
        {
            return Problems.BadRequest(
                HttpContext,
                string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        if (roles.Count > 0)
        {
            _db.Set<IdentityUserRole<Guid>>().AddRange(
                roles.Select(r => new IdentityUserRole<Guid> { UserId = user.Id, RoleId = r.Id }));
        }

        AuditWriter.Add(
            _db,
            _currentUser,
            AuditActions.UserCreated,
            nameof(ApplicationUser),
            user.Id,
            new
            {
                user.Email,
                user.FullName,
                user.DepartmentId,
                Roles = roles.Select(r => r.Name).OrderBy(name => name).ToArray()
            });

        await _db.SaveChangesAsync(ct);

        _permissions.Invalidate(user.Id);

        return CreatedAtAction(
            nameof(GetById),
            new { id = user.Id },
            UserResponse.From(user, roles.Select(r => r.Name!)));
    }

    [HttpPut("{id:guid}/roles")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> SetRoles(
        Guid id,
        [FromBody] UpdateUserRolesRequest request,
        CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;
        var callerId = _currentUser.UserId ?? Guid.Empty;

        if (id == callerId)
        {
            return Problems.Forbidden(HttpContext, "You cannot change your own roles.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct);
        if (user is null)
        {
            return Problems.NotFound(HttpContext, $"User '{id}' was not found.");
        }

        var roles = await ResolveRolesAsync(tenantId, request.Roles, ct);
        if (roles is null)
        {
            return Problems.BadRequest(HttpContext, "One or more roles do not exist in your company.");
        }

        var grants = await PermissionsOfRolesAsync(roles.Select(r => r.Id), ct);
        var guard = await _guard.EnsureCanGrantAsync(callerId, grants, ct);
        if (!guard.IsSuccess)
        {
            return MapResult(guard);
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var adminRole = await GetAdministratorRoleAsync(tenantId, ct);
        var keepsAdmin = adminRole is not null && roles.Any(r => r.Id == adminRole.Id);

        if (adminRole is not null
            && currentRoles.Contains(RoleNames.Administrator)
            && !keepsAdmin
            && await IsLastActiveAdministratorAsync(tenantId, ct))
        {
            return Problems.Conflict(
                HttpContext,
                "The last Administrator cannot lose the Administrator role. Assign another administrator first.");
        }

        var existing = await _db.Set<IdentityUserRole<Guid>>()
            .Where(ur => ur.UserId == id)
            .ToListAsync(ct);
        _db.Set<IdentityUserRole<Guid>>().RemoveRange(existing);
        _db.Set<IdentityUserRole<Guid>>().AddRange(
            roles.Select(r => new IdentityUserRole<Guid> { UserId = id, RoleId = r.Id }));

        AuditWriter.Add(
            _db,
            _currentUser,
            AuditActions.UserRolesUpdated,
            nameof(ApplicationUser),
            user.Id,
            new
            {
                user.Email,
                PreviousRoles = currentRoles.OrderBy(name => name).ToArray(),
                Roles = roles.Select(r => r.Name).OrderBy(name => name).ToArray()
            });

        await _db.SaveChangesAsync(ct);

        _permissions.Invalidate(id);
        await _notifier.PermissionsChangedAsync([id], ct);

        return Ok(UserResponse.From(user, roles.Select(r => r.Name!)));
    }

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;
        var callerId = _currentUser.UserId ?? Guid.Empty;

        if (id == callerId)
        {
            return Problems.Forbidden(HttpContext, "You cannot deactivate yourself.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct);
        if (user is null)
        {
            return Problems.NotFound(HttpContext, $"User '{id}' was not found.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var adminRole = await GetAdministratorRoleAsync(tenantId, ct);
        if (user.IsActive
            && adminRole is not null
            && roles.Contains(RoleNames.Administrator)
            && await IsLastActiveAdministratorAsync(tenantId, ct))
        {
            return Problems.Conflict(HttpContext, "The last Administrator cannot be deactivated.");
        }

        user.IsActive = false;

        AuditWriter.Add(
            _db,
            _currentUser,
            AuditActions.UserDeactivated,
            nameof(ApplicationUser),
            user.Id,
            new { user.Email, user.FullName });

        await _db.SaveChangesAsync(ct);

        _permissions.Invalidate(id);
        await _refreshTokens.RevokeAllForUserAsync(id, ct);
        await _notifier.PermissionsChangedAsync([id], ct);

        return Ok(UserResponse.From(user, roles));
    }

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct);
        if (user is null)
        {
            return Problems.NotFound(HttpContext, $"User '{id}' was not found.");
        }

        user.IsActive = true;

        AuditWriter.Add(
            _db,
            _currentUser,
            AuditActions.UserActivated,
            nameof(ApplicationUser),
            user.Id,
            new { user.Email, user.FullName });

        await _db.SaveChangesAsync(ct);

        _permissions.Invalidate(id);
        await _notifier.PermissionsChangedAsync([id], ct);

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(UserResponse.From(user, roles));
    }

    private async Task<List<ApplicationRole>?> ResolveRolesAsync(
        Guid tenantId,
        IReadOnlyCollection<string> roleNames,
        CancellationToken ct)
    {
        var distinctNames = roleNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctNames.Length == 0)
        {
            return [];
        }

        var normalizedNames = distinctNames.Select(n => n.ToUpperInvariant()).ToArray();
        var roles = await _db.Roles
            .Where(r => r.TenantId == tenantId && r.NormalizedName != null && normalizedNames.Contains(r.NormalizedName))
            .ToListAsync(ct);

        return roles.Count == distinctNames.Length ? roles : null;
    }

    private async Task<IReadOnlyCollection<string>> PermissionsOfRolesAsync(
        IEnumerable<Guid> roleIds,
        CancellationToken ct)
    {
        var ids = roleIds.ToArray();
        return await (
            from rolePermission in _db.RolePermissions
            join permission in _db.Permissions on rolePermission.PermissionId equals permission.Id
            where ids.Contains(rolePermission.RoleId)
            select permission.Code)
            .Distinct()
            .ToListAsync(ct);
    }

    private async Task<ApplicationRole?> GetAdministratorRoleAsync(Guid tenantId, CancellationToken ct)
    {
        var normalized = RoleNames.Administrator.ToUpperInvariant();
        return await _db.Roles
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized, ct);
    }

    private async Task<bool> IsLastActiveAdministratorAsync(Guid tenantId, CancellationToken ct)
    {
        var adminRole = await GetAdministratorRoleAsync(tenantId, ct);
        if (adminRole is null)
        {
            return false;
        }

        var activeAdmins = await (
            from userRole in _db.Set<IdentityUserRole<Guid>>()
            join user in _db.Users on userRole.UserId equals user.Id
            where userRole.RoleId == adminRole.Id && user.TenantId == tenantId && user.IsActive
            select user.Id)
            .Distinct()
            .CountAsync(ct);

        return activeAdmins <= 1;
    }
}
