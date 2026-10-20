using System.Text.Json;
using LeaveRequests.Api.Contracts.Audit;
using LeaveRequests.Api.Data;
using LeaveRequests.Api.Security;
using LeaveRequests.Api.Security.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveRequests.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
public sealed class AuditLogsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditLogsController(AppDbContext db) => _db = db;

    [HttpGet]
    [HasPermission(Permissions.Audit.View)]
    public async Task<IActionResult> List(
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);

        var entries = await _db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);

        return Ok(entries.Select(ToResponse));
    }

    private static AuditLogResponse ToResponse(Domain.Entities.AuditLog entry)
    {
        JsonElement? details = null;
        if (!string.IsNullOrWhiteSpace(entry.Details))
        {
            using var document = JsonDocument.Parse(entry.Details);
            details = document.RootElement.Clone();
        }

        return new AuditLogResponse(
            entry.Id,
            entry.ActorName,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            details,
            entry.CreatedAtUtc);
    }
}
