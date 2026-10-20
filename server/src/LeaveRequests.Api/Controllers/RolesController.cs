using LeaveRequests.Api.Audit;
using LeaveRequests.Api.Common;
using LeaveRequests.Api.Contracts.Roles;
using LeaveRequests.Api.Data;
using LeaveRequests.Api.Domain.Entities;
using LeaveRequests.Api.Realtime;
using LeaveRequests.Api.Security;
using LeaveRequests.Api.Security.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveRequests.Api.Controllers;

[ApiController]
[Route("api/roles")]
public sealed class RolesController : ApiControllerBase
{
    private readonly AppDbContext _db;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _currentUser;
    private readonly PrivilegeGuard _guard;
    private readonly IUserNotifier _notifier;

    public RolesController(
        AppDbContext db,
        RoleManager<ApplicationRole> roleManager,
        IPermissionService permissions,
        ICurrentUser currentUser,
        PrivilegeGuard guard,
        IUserNotifier notifier)
    {
        _db = db;
        _roleManager = roleManager;
        _permissions = permissions;
        _currentUser = currentUser;
        _guard = guard;
        _notifier = notifier;
    }

    [HttpGet]
    [HasPermission(Permissions.Roles.View)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;

        var roles = await _db.Roles
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        var permissionsByRole = await BuildPermissionLookupAsync(roles.Select(r => r.Id), ct);

        return Ok(roles.Select(role => ToResponse(role, permissionsByRole)));
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Roles.View)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;

        var role = await _db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (role is null)
        {
            return Problems.NotFound(HttpContext, $"Role '{id}' was not found.");
        }

        var permissionsByRole = await BuildPermissionLookupAsync([role.Id], ct);
        return Ok(ToResponse(role, permissionsByRole));
    }

    [HttpPost]
    [HasPermission(Permissions.Roles.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;
        var callerId = _currentUser.UserId ?? Guid.Empty;

        var normalized = _roleManager.NormalizeKey(request.Name.Trim());
        if (await _db.Roles.AnyAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized, ct))
        {
            return DuplicateName(request.Name);
        }

        var permissions = await ResolvePermissionsAsync(request.Permissions ?? [], ct);
        if (permissions is null)
        {
            return Problems.BadRequest(HttpContext, "One or more permission codes are unknown.");
        }

        var guard = await _guard.EnsureCanGrantAsync(callerId, permissions.Select(p => p.Code), ct);
        if (!guard.IsSuccess)
        {
            return MapResult(guard);
        }

        var role = new ApplicationRole
        {
            Name = request.Name.Trim(),
            TenantId = tenantId,
            IsSystemRole = false,
            Description = request.Description
        };

        var created = await _roleManager.CreateAsync(role);
        if (!created.Succeeded)
        {
            return Problems.BadRequest(HttpContext, JoinErrors(created));
        }

        _db.RolePermissions.AddRange(permissions.Select(p => new RolePermission
        {
            RoleId = role.Id,
            PermissionId = p.Id
        }));

        AuditWriter.Add(
            _db,
            _currentUser,
            AuditActions.RoleCreated,
            nameof(ApplicationRole),
            role.Id,
            new
            {
                role.Name,
                role.Description,
                Permissions = permissions.Select(p => p.Code).OrderBy(c => c).ToArray()
            });

        await _db.SaveChangesAsync(ct);

        var response = new RoleResponse(
            role.Id,
            role.Name!,
            role.Description,
            role.IsSystemRole,
            permissions.Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray());

        return CreatedAtAction(nameof(GetById), new { id = role.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Roles.Manage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;
        var callerId = _currentUser.UserId ?? Guid.Empty;

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (role is null)
        {
            return Problems.NotFound(HttpContext, $"Role '{id}' was not found.");
        }

        if (role.IsSystemRole)
        {
            return Problems.Forbidden(HttpContext, "System roles cannot be edited.");
        }

        var normalized = _roleManager.NormalizeKey(request.Name.Trim());
        if (await _db.Roles.AnyAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized && r.Id != id, ct))
        {
            return DuplicateName(request.Name);
        }

        var permissions = await ResolvePermissionsAsync(request.Permissions ?? [], ct);
        if (permissions is null)
        {
            return Problems.BadRequest(HttpContext, "One or more permission codes are unknown.");
        }

        var guard = await _guard.EnsureCanGrantAsync(callerId, permissions.Select(p => p.Code), ct);
        if (!guard.IsSuccess)
        {
            return MapResult(guard);
        }

        var previousName = role.Name;
        var previousDescription = role.Description;
        var previousPermissions = await (
            from rolePermission in _db.RolePermissions
            join permission in _db.Permissions on rolePermission.PermissionId equals permission.Id
            where rolePermission.RoleId == role.Id
            select permission.Code)
            .OrderBy(code => code)
            .ToListAsync(ct);

        role.Name = request.Name.Trim();
        role.Description = request.Description;
        var updated = await _roleManager.UpdateAsync(role);
        if (!updated.Succeeded)
        {
            return Problems.BadRequest(HttpContext, JoinErrors(updated));
        }

        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == role.Id).ToListAsync(ct);
        _db.RolePermissions.RemoveRange(existing);
        _db.RolePermissions.AddRange(permissions.Select(p => new RolePermission
        {
            RoleId = role.Id,
            PermissionId = p.Id
        }));

        AuditWriter.Add(
            _db,
            _currentUser,
            AuditActions.RoleUpdated,
            nameof(ApplicationRole),
            role.Id,
            new
            {
                PreviousName = previousName,
                role.Name,
                PreviousDescription = previousDescription,
                role.Description,
                PreviousPermissions = previousPermissions,
                Permissions = permissions.Select(p => p.Code).OrderBy(c => c).ToArray()
            });

        await _db.SaveChangesAsync(ct);

        var affectedUsers = await InvalidateRoleUsersAsync(role.Id, ct);
        await _notifier.PermissionsChangedAsync(affectedUsers, ct);

        var response = new RoleResponse(
            role.Id,
            role.Name!,
            role.Description,
            role.IsSystemRole,
            permissions.Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray());

        return Ok(response);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Roles.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (role is null)
        {
            return Problems.NotFound(HttpContext, $"Role '{id}' was not found.");
        }

        if (role.IsSystemRole)
        {
            return Problems.Forbidden(HttpContext, "System roles cannot be deleted.");
        }

        var inUse = await _db.Set<IdentityUserRole<Guid>>().AnyAsync(ur => ur.RoleId == role.Id, ct);
        if (inUse)
        {
            return Problems.Conflict(
                HttpContext,
                $"The role '{role.Name}' is assigned to one or more users and cannot be deleted.");
        }

        var affectedUsers = await InvalidateRoleUsersAsync(role.Id, ct);

        AuditWriter.Add(
            _db,
            _currentUser,
            AuditActions.RoleDeleted,
            nameof(ApplicationRole),
            role.Id,
            new { role.Name, role.Description });

        var deleted = await _roleManager.DeleteAsync(role);
        if (!deleted.Succeeded)
        {
            return Problems.BadRequest(HttpContext, JoinErrors(deleted));
        }

        await _notifier.PermissionsChangedAsync(affectedUsers, ct);
        return NoContent();
    }

    private async Task<Dictionary<Guid, string[]>> BuildPermissionLookupAsync(
        IEnumerable<Guid> roleIds,
        CancellationToken ct)
    {
        var ids = roleIds.ToArray();
        var rows = await (
            from rolePermission in _db.RolePermissions
            join permission in _db.Permissions on rolePermission.PermissionId equals permission.Id
            where ids.Contains(rolePermission.RoleId)
            select new { rolePermission.RoleId, permission.Code })
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.RoleId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.Code).OrderBy(code => code, StringComparer.Ordinal).ToArray());
    }

    private static RoleResponse ToResponse(ApplicationRole role, Dictionary<Guid, string[]> lookup) =>
        new(
            role.Id,
            role.Name ?? string.Empty,
            role.Description,
            role.IsSystemRole,
            lookup.TryGetValue(role.Id, out var codes) ? codes : []);

    private async Task<List<Permission>?> ResolvePermissionsAsync(
        IReadOnlyCollection<string> codes,
        CancellationToken ct)
    {
        var distinct = codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinct.Length == 0)
        {
            return [];
        }

        var permissions = await _db.Permissions.Where(p => distinct.Contains(p.Code)).ToListAsync(ct);
        return permissions.Count == distinct.Length ? permissions : null;
    }

    private async Task<Guid[]> InvalidateRoleUsersAsync(Guid roleId, CancellationToken ct)
    {
        var userIds = await _db.Set<IdentityUserRole<Guid>>()
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToArrayAsync(ct);

        foreach (var userId in userIds)
        {
            _permissions.Invalidate(userId);
        }

        return userIds;
    }

    private ObjectResult DuplicateName(string name) =>
        Problems.Conflict(
            HttpContext,
            $"A role named '{name}' already exists. Choose a different role name.");

    private static string JoinErrors(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));
}
