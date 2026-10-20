using LeaveRequests.Api.Domain.Entities;

namespace LeaveRequests.Api.Contracts.LeaveRequests;

public sealed record LeaveRequestResponse(
    Guid Id,
    Guid TenantId,
    Guid DepartmentId,
    string DepartmentName,
    Guid EmployeeId,
    string EmployeeName,
    LeaveType Type,
    DateOnly StartDate,
    DateOnly EndDate,
    string Reason,
    LeaveStatus Status,
    Guid? ReviewedById,
    DateTime? ReviewedAtUtc,
    DateTime CreatedAtUtc)
{
    public static LeaveRequestResponse From(
        LeaveRequest request,
        string? employeeName = null,
        string? departmentName = null) =>
        new(
            request.Id,
            request.TenantId,
            request.DepartmentId,
            departmentName ?? request.Department?.Name ?? string.Empty,
            request.EmployeeId,
            employeeName ?? request.Employee?.FullName ?? string.Empty,
            request.Type,
            request.StartDate,
            request.EndDate,
            request.Reason,
            request.Status,
            request.ReviewedById,
            request.ReviewedAtUtc,
            request.CreatedAtUtc);
}
