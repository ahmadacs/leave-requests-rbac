using Microsoft.AspNetCore.Identity;

namespace LeaveRequests.Api.Domain.Entities;

public class ApplicationRole : IdentityRole<Guid>
{
    public Guid TenantId { get; set; }
    public bool IsSystemRole { get; set; }
    public string? Description { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
