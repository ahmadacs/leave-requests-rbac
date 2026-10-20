using System.Text.Json;

namespace LeaveRequests.Api.Contracts.Audit;

public sealed record AuditLogResponse(
    Guid Id,
    string ActorName,
    string Action,
    string EntityType,
    Guid? EntityId,
    JsonElement? Details,
    DateTime CreatedAtUtc);
