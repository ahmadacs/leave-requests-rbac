using System.Text.Json;
using LeaveRequests.Api.Data;
using LeaveRequests.Api.Domain.Entities;
using LeaveRequests.Api.Security;

namespace LeaveRequests.Api.Audit;

public static class AuditWriter
{
    public static void Add(
        AppDbContext db,
        ICurrentUser currentUser,
        string action,
        string entityType,
        Guid? entityId,
        object? details = null)
    {
        var json = details is null
            ? "{}"
            : JsonSerializer.Serialize(details);

        if (json.Length > 4000)
        {
            json = json[..4000];
        }

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = currentUser.TenantId ?? Guid.Empty,
            ActorUserId = currentUser.UserId,
            ActorName = currentUser.Name ?? currentUser.Email ?? "system",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = json,
            CreatedAtUtc = DateTime.UtcNow
        });
    }
}
