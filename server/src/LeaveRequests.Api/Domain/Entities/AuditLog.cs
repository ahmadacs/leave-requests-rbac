namespace LeaveRequests.Api.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string Details { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
