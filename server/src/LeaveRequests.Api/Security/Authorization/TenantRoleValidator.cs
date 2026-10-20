using LeaveRequests.Api.Data;
using LeaveRequests.Api.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LeaveRequests.Api.Security.Authorization;

/// <summary>
/// Replaces Identity's default <see cref="RoleValidator{TRole}"/>.
/// <para>
/// The default validator calls <c>FindByNameAsync</c>, which compares role
/// names across the whole system. Because our role names are only unique per
/// tenant, that default is unsafe here (it would reject "Manager" in a second
/// company). This validator scopes the uniqueness check to the role's tenant.
/// </para>
/// </summary>
public sealed class TenantRoleValidator : IRoleValidator<ApplicationRole>
{
    private readonly AppDbContext _db;

    public TenantRoleValidator(AppDbContext db) => _db = db;

    public async Task<IdentityResult> ValidateAsync(RoleManager<ApplicationRole> manager, ApplicationRole role)
    {
        var errors = new List<IdentityError>();

        if (string.IsNullOrWhiteSpace(role.Name))
        {
            errors.Add(new IdentityError
            {
                Code = "InvalidRoleName",
                Description = "A role name is required."
            });
            return IdentityResult.Failed(errors.ToArray());
        }

        var normalized = manager.NormalizeKey(role.Name);
        var duplicate = await _db.Roles.AnyAsync(r =>
            r.TenantId == role.TenantId
            && r.NormalizedName == normalized
            && r.Id != role.Id);

        if (duplicate)
        {
            errors.Add(new IdentityError
            {
                Code = "DuplicateRoleName",
                Description = $"A role named '{role.Name}' already exists. Choose a different role name."
            });
        }

        return errors.Count == 0
            ? IdentityResult.Success
            : IdentityResult.Failed(errors.ToArray());
    }
}
