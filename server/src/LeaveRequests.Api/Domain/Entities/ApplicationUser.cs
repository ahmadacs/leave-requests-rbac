using Microsoft.AspNetCore.Identity;

namespace LeaveRequests.Api.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public Tenant Tenant { get; set; } = null!;
    public Department? Department { get; set; }

    public ICollection<LeaveRequest> LeaveRequests { get; set; } = new List<LeaveRequest>();
}
