namespace LeaveRequests.Api.Security;

public interface IPermissionService
{
    /// <summary>
    /// Effective permissions for a user, resolved from the database and cached
    /// per user. A deactivated (or unknown) user has no permissions.
    /// </summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken ct = default);

    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default);

    /// <summary>Clears the cached permissions of a single user.</summary>
    void Invalidate(Guid userId);
}
