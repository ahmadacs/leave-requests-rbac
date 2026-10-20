using LeaveRequests.Api.Contracts.Permissions;
using LeaveRequests.Api.Data;
using LeaveRequests.Api.Security;
using LeaveRequests.Api.Security.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveRequests.Api.Controllers;

[ApiController]
[Route("api/permissions")]
public sealed class PermissionsController : ApiControllerBase
{
    private readonly AppDbContext _db;

    public PermissionsController(AppDbContext db) => _db = db;

    [HttpGet]
    [HasPermission(Permissions.Roles.View)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var permissions = await _db.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Code)
            .ToListAsync(ct);

        return Ok(permissions.Select(PermissionResponse.From));
    }
}
