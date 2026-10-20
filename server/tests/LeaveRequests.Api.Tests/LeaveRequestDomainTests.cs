using LeaveRequests.Api.Common;
using LeaveRequests.Api.Domain.Entities;
using Xunit;

namespace LeaveRequests.Api.Tests;

public sealed class LeaveRequestDomainTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ManagerId = Guid.NewGuid();
    private static readonly Guid DepartmentId = Guid.NewGuid();
    private static readonly Guid OtherDepartmentId = Guid.NewGuid();

    [Fact]
    public void NewRequest_RejectsEndDateBeforeStartDate()
    {
        var result = LeaveRequest.ValidateNewRequest(
            new DateOnly(2026, 11, 10),
            new DateOnly(2026, 11, 9),
            []);

        Assert.Equal(ResultStatus.Validation, result.Status);
    }

    [Theory]
    [InlineData(LeaveStatus.Pending)]
    [InlineData(LeaveStatus.Approved)]
    public void NewRequest_RejectsOverlapWithActiveRequest(LeaveStatus status)
    {
        var existing = Request(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 6),
            status);

        var result = LeaveRequest.ValidateNewRequest(
            new DateOnly(2026, 11, 6),
            new DateOnly(2026, 11, 10),
            [existing]);

        Assert.Equal(ResultStatus.Conflict, result.Status);
    }

    [Theory]
    [InlineData(LeaveStatus.Rejected)]
    [InlineData(LeaveStatus.Cancelled)]
    public void NewRequest_IgnoresOverlapWithInactiveRequest(LeaveStatus status)
    {
        var existing = Request(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 6),
            status);

        var result = LeaveRequest.ValidateNewRequest(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 6),
            [existing]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void NewRequest_AllowsAdjacentDates()
    {
        var existing = Request(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 6),
            LeaveStatus.Pending);

        var result = LeaveRequest.ValidateNewRequest(
            new DateOnly(2026, 11, 7),
            new DateOnly(2026, 11, 9),
            [existing]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Review_RejectsOwnRequest()
    {
        var request = Request(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 6),
            LeaveStatus.Pending,
            employeeId: ManagerId);

        var result = request.Approve(
            ManagerId,
            DepartmentId,
            canReviewAnyDepartment: true,
            canApproveLongRequests: true,
            DateTime.UtcNow);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(LeaveStatus.Pending, request.Status);
    }

    [Fact]
    public void Review_ManagerCannotReviewAnotherDepartment()
    {
        var request = Request(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 6),
            LeaveStatus.Pending);

        var result = request.Reject(
            ManagerId,
            OtherDepartmentId,
            canReviewAnyDepartment: false,
            canApproveLongRequests: false,
            DateTime.UtcNow);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(LeaveStatus.Pending, request.Status);
    }

    [Fact]
    public void Review_AdministratorCanReviewAnotherDepartment()
    {
        var request = Request(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 6),
            LeaveStatus.Pending);

        var result = request.Approve(
            ManagerId,
            OtherDepartmentId,
            canReviewAnyDepartment: true,
            canApproveLongRequests: true,
            DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(LeaveStatus.Approved, request.Status);
        Assert.Equal(ManagerId, request.ReviewedById);
        Assert.NotNull(request.ReviewedAtUtc);
    }

    [Fact]
    public void Review_ManagerCanApproveRequestUpToTenDays()
    {
        var request = Request(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 10),
            LeaveStatus.Pending);

        var result = request.Approve(
            ManagerId,
            DepartmentId,
            canReviewAnyDepartment: false,
            canApproveLongRequests: false,
            DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, request.DurationDays);
    }

    [Fact]
    public void Review_ManagerCannotApproveRequestLongerThanTenDays()
    {
        var request = Request(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 11),
            LeaveStatus.Pending);

        var result = request.Approve(
            ManagerId,
            DepartmentId,
            canReviewAnyDepartment: false,
            canApproveLongRequests: false,
            DateTime.UtcNow);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(LeaveStatus.Pending, request.Status);
        Assert.Equal(11, request.DurationDays);
    }

    [Fact]
    public void Review_HrCanApproveRequestLongerThanTenDays()
    {
        var request = Request(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 11),
            LeaveStatus.Pending);

        var result = request.Approve(
            ManagerId,
            DepartmentId,
            canReviewAnyDepartment: true,
            canApproveLongRequests: true,
            DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(LeaveStatus.Approved, request.Status);
    }

    [Fact]
    public void Review_RejectsRequestThatIsNoLongerPending()
    {
        var request = Request(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 6),
            LeaveStatus.Approved);

        var result = request.Reject(
            ManagerId,
            DepartmentId,
            canReviewAnyDepartment: false,
            canApproveLongRequests: false,
            DateTime.UtcNow);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(LeaveStatus.Approved, request.Status);
    }

    [Fact]
    public void Cancel_RejectsAnotherEmployeesRequest()
    {
        var request = Request(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 6),
            LeaveStatus.Pending);

        var result = request.Cancel(ManagerId);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(LeaveStatus.Pending, request.Status);
    }

    [Fact]
    public void Cancel_RejectsNonPendingRequest()
    {
        var request = Request(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 6),
            LeaveStatus.Approved);

        var result = request.Cancel(EmployeeId);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(LeaveStatus.Approved, request.Status);
    }

    [Fact]
    public void Cancel_OwnPendingRequestSucceeds()
    {
        var request = Request(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 6),
            LeaveStatus.Pending);

        var result = request.Cancel(EmployeeId);

        Assert.True(result.IsSuccess);
        Assert.Equal(LeaveStatus.Cancelled, request.Status);
    }

    private static LeaveRequest Request(
        DateOnly startDate,
        DateOnly endDate,
        LeaveStatus status,
        Guid? employeeId = null) =>
        new()
        {
            TenantId = Guid.NewGuid(),
            DepartmentId = DepartmentId,
            EmployeeId = employeeId ?? EmployeeId,
            Type = LeaveType.Annual,
            StartDate = startDate,
            EndDate = endDate,
            Status = status
        };
}
