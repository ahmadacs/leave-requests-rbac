using LeaveRequests.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LeaveRequests.Api.Security;

public sealed class PermissionService : IPermissionService
{
    private static readonly IReadOnlySet<string> NoPermissions = new HashSet<string>();

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    public PermissionService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey(userId), out IReadOnlySet<string>? cached) && cached is not null)
        {
            return cached;
        }

        var permissions = await LoadAsync(userId, ct);
        _cache.Set(CacheKey(userId), permissions, _cacheDuration);
        return permissions;
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default)
    {
        var permissions = await GetPermissionsAsync(userId, ct);
        return permissions.Contains(permission);
    }

    public void Invalidate(Guid userId) => _cache.Remove(CacheKey(userId));

    private async Task<IReadOnlySet<string>> LoadAsync(Guid userId, CancellationToken ct)
    {
        var isActive = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (bool?)u.IsActive)
            .FirstOrDefaultAsync(ct);

        if (isActive is not true)
        {
            return NoPermissions;
        }

        var codes = await (
            from userRole in _db.Set<IdentityUserRole<Guid>>()
            join rolePermission in _db.RolePermissions on userRole.RoleId equals rolePermission.RoleId
            join permission in _db.Permissions on rolePermission.PermissionId equals permission.Id
            where userRole.UserId == userId
            select permission.Code)
            .Distinct()
            .ToListAsync(ct);

        return codes.ToHashSet(StringComparer.Ordinal);
    }

    private static string CacheKey(Guid userId) => $"permissions:{userId}";
}
