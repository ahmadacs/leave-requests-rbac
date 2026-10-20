using System.Text;
using LeaveRequests.Api.Common;
using LeaveRequests.Api.Contracts.LeaveRequests;
using LeaveRequests.Api.Data;
using LeaveRequests.Api.Domain.Entities;
using LeaveRequests.Api.Security;
using LeaveRequests.Api.Security.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveRequests.Api.Controllers;

[ApiController]
[Route("api/leave-requests")]
public sealed class LeaveRequestsController : ApiControllerBase
{
    private static readonly string[] ValidScopes = ["own", "team", "all"];

    private readonly AppDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _currentUser;

    public LeaveRequestsController(
        AppDbContext db,
        IPermissionService permissions,
        ICurrentUser currentUser)
    {
        _db = db;
        _permissions = permissions;
        _currentUser = currentUser;
    }

    // Scope-dependent endpoints are guarded with [AuthenticatedOnly] and then
    // resolved against the caller's view permissions, because the required
    // permission depends on the requested scope.
    [HttpGet]
    [AuthenticatedOnly]
    public async Task<IActionResult> List(
        [FromQuery] string? scope,
        [FromQuery] LeaveStatus? status,
        CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Problems.Unauthorized(HttpContext, "Authentication is required.");
        }

        var permissions = await _permissions.GetPermissionsAsync(userId, ct);
        var hasOwn = permissions.Contains(Permissions.Leaves.ViewOwn);
        var hasTeam = permissions.Contains(Permissions.Leaves.ViewTeam);
        var hasAll = permissions.Contains(Permissions.Leaves.ViewAll);

        IQueryable<LeaveRequest> query = _db.LeaveRequests
            .AsNoTracking()
            .Include(r => r.Employee)
            .Include(r => r.Department);

        var requestedScope = string.IsNullOrWhiteSpace(scope) ? null : scope.Trim().ToLowerInvariant();
        if (requestedScope is not null && !ValidScopes.Contains(requestedScope))
        {
            return Problems.BadRequest(HttpContext, "scope must be one of: own, team, all.");
        }

        // When no scope is given, pick the widest the caller is allowed.
        requestedScope ??= hasAll ? "all" : hasTeam ? "team" : "own";

        switch (requestedScope)
        {
            case "all":
                if (!hasAll)
                {
                    return Missing(Permissions.Leaves.ViewAll);
                }
                break;

            case "team":
                if (!hasTeam)
                {
                    return Missing(Permissions.Leaves.ViewTeam);
                }
                if (_currentUser.DepartmentId is not { } departmentId)
                {
                    return Ok(Array.Empty<LeaveRequestResponse>());
                }
                query = query.Where(r => r.DepartmentId == departmentId);
                break;

            default:
                if (!hasOwn)
                {
                    return Missing(Permissions.Leaves.ViewOwn);
                }
                query = query.Where(r => r.EmployeeId == userId);
                break;
        }

        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(items.Select(item => LeaveRequestResponse.From(item)));
    }

    [HttpGet("{id:guid}")]
    [AuthenticatedOnly]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Problems.Unauthorized(HttpContext, "Authentication is required.");
        }

        var permissions = await _permissions.GetPermissionsAsync(userId, ct);
        var request = await FindVisibleAsync(id, userId, permissions, tracking: false, ct);
        if (request is null)
        {
            return NotFoundProblem(id);
        }

        return Ok(LeaveRequestResponse.From(request));
    }

    [HttpPost]
    [HasPermission(Permissions.Leaves.Create)]
    public async Task<IActionResult> Create([FromBody] CreateLeaveRequestRequest request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Problems.Unauthorized(HttpContext, "Authentication is required.");
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Problems.Unauthorized(HttpContext, "Authentication is required.");
        }

        if (user.DepartmentId is not { } departmentId)
        {
            return Problems.Conflict(HttpContext, "You must belong to a department to request leave.");
        }

        var activeRequests = await _db.LeaveRequests
            .AsNoTracking()
            .Where(r => r.EmployeeId == userId
                && (r.Status == LeaveStatus.Pending || r.Status == LeaveStatus.Approved))
            .ToListAsync(ct);

        var rule = LeaveRequest.ValidateNewRequest(
            request.StartDate,
            request.EndDate,
            activeRequests);
        if (!rule.IsSuccess)
        {
            return MapResult(rule);
        }

        var entity = new LeaveRequest
        {
            TenantId = user.TenantId,
            DepartmentId = departmentId,
            EmployeeId = userId,
            Type = request.Type,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = request.Reason,
            Status = LeaveStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.LeaveRequests.Add(entity);
        await _db.SaveChangesAsync(ct);

        var departmentName = await _db.Departments
            .AsNoTracking()
            .Where(d => d.Id == departmentId)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return CreatedAtAction(
            nameof(GetById),
            new { id = entity.Id },
            LeaveRequestResponse.From(entity, user.FullName, departmentName));
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Leaves.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Problems.Unauthorized(HttpContext, "Authentication is required.");
        }

        var request = await FindOwnedAsync(id, userId, ct);
        if (request is null)
        {
            return NotFoundProblem(id);
        }

        var result = request.Cancel(userId);
        if (!result.IsSuccess)
        {
            return MapResult(result);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(LeaveRequestResponse.From(request));
    }

    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.Leaves.Approve)]
    public Task<IActionResult> Approve(Guid id, CancellationToken ct) =>
        ReviewAsync(id, approve: true, ct);

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.Leaves.Approve)]
    public Task<IActionResult> Reject(Guid id, CancellationToken ct) =>
        ReviewAsync(id, approve: false, ct);

    // Two [HasPermission] attributes = AND: the caller needs both.
    [HttpGet("export")]
    [HasPermission(Permissions.Leaves.ViewAll)]
    [HasPermission(Permissions.Leaves.Export)]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var items = await _db.LeaveRequests
            .AsNoTracking()
            .Include(r => r.Employee)
            .Include(r => r.Department)
            .OrderBy(r => r.StartDate)
            .ToListAsync(ct);

        var csv = new StringBuilder();
        csv.AppendLine("Id,Employee,Department,Type,StartDate,EndDate,Status,Reason");
        foreach (var item in items)
        {
            csv.AppendLine(string.Join(',',
                Escape(item.Id.ToString()),
                Escape(item.Employee?.FullName ?? string.Empty),
                Escape(item.Department?.Name ?? string.Empty),
                Escape(item.Type.ToString()),
                Escape(item.StartDate.ToString("yyyy-MM-dd")),
                Escape(item.EndDate.ToString("yyyy-MM-dd")),
                Escape(item.Status.ToString()),
                Escape(item.Reason)));
        }

        return File(
            Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv",
            "leave-requests.csv");
    }

    private async Task<IActionResult> ReviewAsync(Guid id, bool approve, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Problems.Unauthorized(HttpContext, "Authentication is required.");
        }

        var permissions = await _permissions.GetPermissionsAsync(userId, ct);
        var request = await FindVisibleAsync(id, userId, permissions, tracking: true, ct);
        if (request is null)
        {
            return NotFoundProblem(id);
        }

        var canReviewAnyDepartment = permissions.Contains(Permissions.Leaves.ViewAll);
        var canApproveLongRequests = permissions.Contains(Permissions.Leaves.ApproveLong);
        var now = DateTime.UtcNow;
        var result = approve
            ? request.Approve(
                userId,
                _currentUser.DepartmentId,
                canReviewAnyDepartment,
                canApproveLongRequests,
                now)
            : request.Reject(
                userId,
                _currentUser.DepartmentId,
                canReviewAnyDepartment,
                canApproveLongRequests,
                now);

        if (!result.IsSuccess)
        {
            return MapResult(result);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(LeaveRequestResponse.From(request));
    }

    /// <summary>
    /// Loads a request if it falls inside one of the caller's view scopes.
    /// Anything outside those scopes (including other tenants, filtered by the
    /// global query filter) is treated as "not found".
    /// </summary>
    private async Task<LeaveRequest?> FindVisibleAsync(
        Guid id,
        Guid userId,
        IReadOnlySet<string> permissions,
        bool tracking,
        CancellationToken ct)
    {
        IQueryable<LeaveRequest> query = tracking ? _db.LeaveRequests : _db.LeaveRequests.AsNoTracking();
        query = query.Include(r => r.Employee).Include(r => r.Department);

        var hasAll = permissions.Contains(Permissions.Leaves.ViewAll);
        var hasTeam = permissions.Contains(Permissions.Leaves.ViewTeam);
        var hasOwn = permissions.Contains(Permissions.Leaves.ViewOwn);

        if (!hasAll)
        {
            var departmentId = _currentUser.DepartmentId;

            if (hasTeam && departmentId is not null && hasOwn)
            {
                query = query.Where(r => r.DepartmentId == departmentId || r.EmployeeId == userId);
            }
            else if (hasTeam && departmentId is not null)
            {
                query = query.Where(r => r.DepartmentId == departmentId);
            }
            else if (hasOwn)
            {
                query = query.Where(r => r.EmployeeId == userId);
            }
            else
            {
                // No view scope at all: the row is not visible. This covers a
                // deactivated user and a role that lost leaves.view.own.
                return null;
            }
        }

        return await query.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    /// <summary>
    /// Loads a request owned by the caller. Used by cancel, where ownership is
    /// the scope rule; a caller needs leaves.cancel but not necessarily
    /// leaves.view.own.
    /// </summary>
    private async Task<LeaveRequest?> FindOwnedAsync(Guid id, Guid userId, CancellationToken ct) =>
        await _db.LeaveRequests
            .Include(r => r.Employee)
            .Include(r => r.Department)
            .FirstOrDefaultAsync(r => r.Id == id && r.EmployeeId == userId, ct);

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private ObjectResult NotFoundProblem(Guid id) =>
        Problems.NotFound(HttpContext, $"Leave request '{id}' was not found.");
}
