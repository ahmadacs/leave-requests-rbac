using LeaveRequests.Api.Domain.Entities;
using LeaveRequests.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LeaveRequests.Api.Data;

/// <summary>
/// Idempotent startup seeding: sync the permission catalogue, create the demo
/// tenants, departments, system roles, users and a handful of leave requests.
/// Safe to run on every startup.
/// </summary>
public static class DbSeeder
{
    /// <summary>Password shared by every demo account (documented in the README).</summary>
    public const string DemoPassword = "Passw0rd!";

    private sealed record UserSeed(
        string Email,
        string FullName,
        string Role,
        string? Department);

    private sealed record TenantSeed(
        string Name,
        string Slug,
        IReadOnlyList<string> Departments,
        IReadOnlyList<UserSeed> Users,
        IReadOnlyList<LeaveRequestSeed> Requests);

    private sealed record LeaveRequestSeed(
        string EmployeeEmail,
        LeaveType Type,
        DateOnly StartDate,
        DateOnly EndDate,
        string Reason,
        LeaveStatus Status,
        string? ReviewedByEmail);

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        await SyncPermissionsAsync(db);

        foreach (var seed in Tenants)
        {
            await SeedTenantAsync(db, userManager, roleManager, seed);
        }
    }

    /// <summary>
    /// Inserts any permission code that is not in the database yet and keeps
    /// the description/group up to date. Never deletes permissions.
    /// </summary>
    private static async Task SyncPermissionsAsync(AppDbContext db)
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Code);
        var added = false;

        foreach (var definition in Permissions.All)
        {
            if (existing.TryGetValue(definition.Code, out var permission))
            {
                permission.Group = definition.Group;
                permission.Description = definition.Description;
            }
            else
            {
                db.Permissions.Add(new Permission
                {
                    Code = definition.Code,
                    Group = definition.Group,
                    Description = definition.Description
                });
                added = true;
            }
        }

        if (added || db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedTenantAsync(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        TenantSeed seed)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == seed.Slug);
        var isNewTenant = tenant is null;
        if (tenant is null)
        {
            tenant = new Tenant { Name = seed.Name, Slug = seed.Slug };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        // An existing tenant that already has users is treated as live data.
        // Do not recreate deleted/renamed default roles or re-apply the
        // default permission matrix: those are admin-editable. We only keep
        // the Administrator system role complete and repair demo departments
        // that are still missing.
        if (!isNewTenant && await db.Users.AnyAsync(u => u.TenantId == tenant.Id))
        {
            await EnsureAdministratorRoleAsync(db, roleManager, tenant);
            await EnsureDemoUserDepartmentsAsync(db, tenant, seed);
            return;
        }

        var departments = new Dictionary<string, Department>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in seed.Departments)
        {
            var department = await db.Departments
                .FirstOrDefaultAsync(d => d.TenantId == tenant.Id && d.Name == name);
            if (department is null)
            {
                department = new Department { TenantId = tenant.Id, Name = name };
                db.Departments.Add(department);
                await db.SaveChangesAsync();
            }

            departments[name] = department;
        }

        var roles = await EnsureRolesAsync(db, roleManager, tenant);

        var users = new Dictionary<string, ApplicationUser>(StringComparer.OrdinalIgnoreCase);
        foreach (var userSeed in seed.Users)
        {
            var user = await EnsureUserAsync(db, userManager, tenant, departments, userSeed, roles);
            users[userSeed.Email] = user;
        }

        await SeedLeaveRequestsAsync(db, tenant, departments, users, seed.Requests);
    }

    /// <summary>Creates the four system roles for a tenant and assigns the matrix.</summary>
    private static async Task<Dictionary<string, ApplicationRole>> EnsureRolesAsync(
        AppDbContext db,
        RoleManager<ApplicationRole> roleManager,
        Tenant tenant)
    {
        var roles = new Dictionary<string, ApplicationRole>(StringComparer.OrdinalIgnoreCase);

        foreach (var roleName in RoleNames.All)
        {
            var normalized = roleManager.NormalizeKey(roleName);
            var role = await db.Roles
                .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.NormalizedName == normalized);

            if (role is null)
            {
                role = new ApplicationRole
                {
                    Name = roleName,
                    TenantId = tenant.Id,
                    IsSystemRole = roleName == RoleNames.Administrator,
                    Description = $"Built-in {roleName} role"
                };
                var result = await roleManager.CreateAsync(role);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to create role '{roleName}' for tenant '{tenant.Name}': " +
                        string.Join("; ", result.Errors.Select(e => e.Description)));
                }
            }

            roles[roleName] = role;
        }

        await SyncRolePermissionsAsync(db, tenant, roles);
        return roles;
    }

    /// <summary>
    /// Ensures the Administrator system role exists and holds every permission
    /// in the catalogue, even for tenants whose other roles/users are left
    /// untouched on upgrade.
    /// </summary>
    private static async Task EnsureAdministratorRoleAsync(
        AppDbContext db,
        RoleManager<ApplicationRole> roleManager,
        Tenant tenant)
    {
        var normalized = roleManager.NormalizeKey(RoleNames.Administrator);
        var role = await db.Roles
            .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.NormalizedName == normalized);

        if (role is null)
        {
            role = new ApplicationRole
            {
                Name = RoleNames.Administrator,
                TenantId = tenant.Id,
                IsSystemRole = true,
                Description = $"Built-in {RoleNames.Administrator} role"
            };

            var created = await roleManager.CreateAsync(role);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create the Administrator role for tenant '{tenant.Name}': " +
                    string.Join("; ", created.Errors.Select(e => e.Description)));
            }
        }
        else if (!role.IsSystemRole)
        {
            role.IsSystemRole = true;
            var updated = await roleManager.UpdateAsync(role);
            if (!updated.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to mark the Administrator role as a system role for tenant '{tenant.Name}': " +
                    string.Join("; ", updated.Errors.Select(e => e.Description)));
            }
        }

        var permissionIds = await db.Permissions.Select(p => p.Id).ToListAsync();
        var currentIds = await db.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        var missing = permissionIds.Except(currentIds).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        db.RolePermissions.AddRange(missing.Select(permissionId => new RolePermission
        {
            RoleId = role.Id,
            PermissionId = permissionId
        }));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Fills in a department for demo accounts that predate the department
    /// seed (HR/Admin used to be tenant-wide). Real admin changes are kept:
    /// only users whose DepartmentId is still null are touched.
    /// </summary>
    private static async Task EnsureDemoUserDepartmentsAsync(
        AppDbContext db,
        Tenant tenant,
        TenantSeed seed)
    {
        var changed = false;

        foreach (var userSeed in seed.Users.Where(user => user.Department is not null))
        {
            var user = await db.Users.FirstOrDefaultAsync(u =>
                u.TenantId == tenant.Id && u.Email == userSeed.Email);
            if (user is null || user.DepartmentId is not null)
            {
                continue;
            }

            var departmentName = userSeed.Department!;
            var department = await db.Departments.FirstOrDefaultAsync(d =>
                d.TenantId == tenant.Id && d.Name == departmentName);

            if (department is null)
            {
                department = new Department { TenantId = tenant.Id, Name = departmentName };
                db.Departments.Add(department);
                await db.SaveChangesAsync();
            }

            user.DepartmentId = department.Id;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Brings the system roles' permission sets in line with the matrix. The
    /// Administrator role always receives every permission in the catalogue.
    /// </summary>
    private static async Task SyncRolePermissionsAsync(
        AppDbContext db,
        Tenant tenant,
        IReadOnlyDictionary<string, ApplicationRole> roles)
    {
        var permissions = await db.Permissions.ToDictionaryAsync(p => p.Code);

        var desired = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [RoleNames.Administrator] = Permissions.All.Select(p => p.Code).ToArray(),
            [RoleNames.HR] =
            [
                Permissions.Leaves.ViewOwn,
                Permissions.Leaves.Create,
                Permissions.Leaves.Cancel,
                Permissions.Leaves.ViewAll,
                Permissions.Leaves.Approve,
                Permissions.Leaves.ApproveLong,
                Permissions.Leaves.Export
            ],
            [RoleNames.Manager] =
            [
                Permissions.Leaves.ViewOwn,
                Permissions.Leaves.Create,
                Permissions.Leaves.Cancel,
                Permissions.Leaves.ViewTeam,
                Permissions.Leaves.Approve
            ],
            [RoleNames.Employee] =
            [
                Permissions.Leaves.ViewOwn,
                Permissions.Leaves.Create,
                Permissions.Leaves.Cancel
            ]
        };

        foreach (var (roleName, codes) in desired)
        {
            var role = roles[roleName];
            var current = await db.RolePermissions
                .Where(rp => rp.RoleId == role.Id)
                .Select(rp => rp.PermissionId)
                .ToHashSetAsync();

            foreach (var code in codes)
            {
                if (!permissions.TryGetValue(code, out var permission))
                {
                    continue;
                }

                if (!current.Contains(permission.Id))
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = permission.Id
                    });
                }
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        Tenant tenant,
        IReadOnlyDictionary<string, Department> departments,
        UserSeed seed,
        IReadOnlyDictionary<string, ApplicationRole> roles)
    {
        var user = await userManager.FindByEmailAsync(seed.Email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = seed.Email,
                Email = seed.Email,
                EmailConfirmed = true,
                FullName = seed.FullName,
                TenantId = tenant.Id,
                DepartmentId = seed.Department is not null && departments.TryGetValue(seed.Department, out var d) ? d.Id : null,
                IsActive = true,
                LockoutEnabled = true
            };

            var result = await userManager.CreateAsync(user, DemoPassword);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create user '{seed.Email}': " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        await AssignRoleByIdAsync(db, user.Id, roles[seed.Role].Id);
        return user;
    }

    /// <summary>
    /// Links a user to a role by id. We deliberately avoid
    /// <c>UserManager.AddToRoleAsync(user, name)</c> because role names are
    /// unique per tenant, not globally, and the name lookup is ambiguous.
    /// </summary>
    private static async Task AssignRoleByIdAsync(AppDbContext db, Guid userId, Guid roleId)
    {
        var exists = await db.Set<IdentityUserRole<Guid>>()
            .AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
        if (exists)
        {
            return;
        }

        db.Set<IdentityUserRole<Guid>>().Add(new IdentityUserRole<Guid>
        {
            UserId = userId,
            RoleId = roleId
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedLeaveRequestsAsync(
        AppDbContext db,
        Tenant tenant,
        IReadOnlyDictionary<string, Department> departments,
        IReadOnlyDictionary<string, ApplicationUser> users,
        IReadOnlyList<LeaveRequestSeed> seeds)
    {
        foreach (var seed in seeds)
        {
            if (!users.TryGetValue(seed.EmployeeEmail, out var employee))
            {
                continue;
            }

            var exists = await db.LeaveRequests.AnyAsync(r =>
                r.EmployeeId == employee.Id &&
                r.StartDate == seed.StartDate &&
                r.EndDate == seed.EndDate);
            if (exists)
            {
                continue;
            }

            ApplicationUser? reviewer = null;
            if (seed.ReviewedByEmail is not null)
            {
                users.TryGetValue(seed.ReviewedByEmail, out reviewer);
            }

            var reviewedAt = seed.Status is LeaveStatus.Approved or LeaveStatus.Rejected
                ? DateTime.UtcNow
                : (DateTime?)null;

            db.LeaveRequests.Add(new LeaveRequest
            {
                TenantId = tenant.Id,
                DepartmentId = employee.DepartmentId!.Value,
                EmployeeId = employee.Id,
                Type = seed.Type,
                StartDate = seed.StartDate,
                EndDate = seed.EndDate,
                Reason = seed.Reason,
                Status = seed.Status,
                ReviewedById = reviewer?.Id,
                ReviewedAtUtc = reviewer is not null ? reviewedAt : null,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }
    }

    private static readonly IReadOnlyList<TenantSeed> Tenants =
    [
        new(
            Name: "Northwind",
            Slug: "northwind",
            Departments: ["Sales", "Finance"],
            Users:
            [
                // Admin holds leaves.view.all and can review across departments.
                // HR holds leaves.create (self-service) and belongs to Sales so
                // the same-department review rule is demonstrable.
                new("admin@northwind.test", "Nadia Admin", RoleNames.Administrator, "Finance"),
                new("hr@northwind.test", "Hana HR", RoleNames.HR, "Sales"),
                new("sales.manager@northwind.test", "Sam Manager", RoleNames.Manager, "Sales"),
                new("sales.employee1@northwind.test", "Sara Employee", RoleNames.Employee, "Sales"),
                new("sales.employee2@northwind.test", "Sven Employee", RoleNames.Employee, "Sales"),
                new("finance.manager@northwind.test", "Faisal Manager", RoleNames.Manager, "Finance"),
                new("finance.employee1@northwind.test", "Fatima Employee", RoleNames.Employee, "Finance"),
                new("finance.employee2@northwind.test", "Fadi Employee", RoleNames.Employee, "Finance"),
            ],
            Requests:
            [
                new("sales.employee1@northwind.test", LeaveType.Annual, new(2026, 11, 2), new(2026, 11, 6), "Family trip", LeaveStatus.Pending, null),
                new("sales.employee1@northwind.test", LeaveType.Sick, new(2026, 9, 10), new(2026, 9, 11), "Flu", LeaveStatus.Approved, "sales.manager@northwind.test"),
                new("sales.employee2@northwind.test", LeaveType.Annual, new(2026, 12, 21), new(2026, 12, 31), "Winter holidays", LeaveStatus.Pending, null),
                new("finance.employee1@northwind.test", LeaveType.Annual, new(2026, 10, 5), new(2026, 10, 9), "Wedding", LeaveStatus.Rejected, "finance.manager@northwind.test"),
                new("finance.employee2@northwind.test", LeaveType.Unpaid, new(2026, 11, 16), new(2026, 11, 20), "Personal matters", LeaveStatus.Cancelled, null),
                // Own pending request for the manager, so the "cannot approve
                // your own request" rule is demonstrable.
                new("sales.manager@northwind.test", LeaveType.Annual, new(2026, 11, 9), new(2026, 11, 10), "Personal day", LeaveStatus.Pending, null),
            ]),
        new(
            Name: "Contoso",
            Slug: "contoso",
            Departments: ["General"],
            Users:
            [
                new("admin@contoso.test", "Cora Admin", RoleNames.Administrator, "General"),
                new("manager@contoso.test", "Mona Manager", RoleNames.Manager, "General"),
                new("employee@contoso.test", "Omar Employee", RoleNames.Employee, "General"),
            ],
            Requests:
            [
                new("employee@contoso.test", LeaveType.Annual, new(2026, 11, 23), new(2026, 11, 27), "Vacation", LeaveStatus.Pending, null),
                new("employee@contoso.test", LeaveType.Sick, new(2026, 8, 3), new(2026, 8, 4), "Cold", LeaveStatus.Approved, "manager@contoso.test"),
            ]),
    ];
}
