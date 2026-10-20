using LeaveRequests.Api.Common;
using LeaveRequests.Api.Contracts.Departments;
using LeaveRequests.Api.Data;
using LeaveRequests.Api.Security;
using LeaveRequests.Api.Security.Authentication;
using LeaveRequests.Api.Security.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveRequests.Api.Controllers;

[ApiController]
[Route("api/departments")]
public sealed class DepartmentsController : ApiControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DepartmentsController(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? Guid.Empty;

        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentResponse(d.Id, d.Name))
            .ToListAsync(ct);

        return Ok(departments);
    }
}
