using LeaveRequests.Api.Common;

namespace LeaveRequests.Api.Domain.Entities;

public class LeaveRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid EmployeeId { get; set; }
    public LeaveType Type { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public Guid? ReviewedById { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public int DurationDays => EndDate.DayNumber - StartDate.DayNumber + 1;

    public const int ManagerApprovalLimitDays = 10;

    public Tenant Tenant { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public ApplicationUser Employee { get; set; } = null!;
    public ApplicationUser? ReviewedBy { get; set; }

    // ------------------------------------------------------------------
    // Business rules — these live in the domain, not the controller.
    // ------------------------------------------------------------------

    /// <summary>
    /// Validates a new request before it is created: the date range must be
    /// valid and it must not overlap another Pending/Approved request of the
    /// same employee. The controller only loads the candidate rows; the rule
    /// itself lives here.
    /// </summary>
    public static Result ValidateNewRequest(
        DateOnly startDate,
        DateOnly endDate,
        IEnumerable<LeaveRequest> existingRequests)
    {
        if (endDate < startDate)
        {
            return Result.Validation("EndDate must be on or after StartDate.");
        }

        if (existingRequests.Any(existing => existing.Overlaps(startDate, endDate)))
        {
            return Result.Conflict(
                "This request overlaps an existing pending or approved leave request.");
        }

        return Result.Success();
    }

    /// <summary>Inclusive overlap between this request and the given dates.</summary>
    public bool Overlaps(DateOnly startDate, DateOnly endDate) =>
        Status is LeaveStatus.Pending or LeaveStatus.Approved
        && StartDate <= endDate
        && startDate <= EndDate;

    public Result Approve(
        Guid reviewerId,
        Guid? reviewerDepartmentId,
        bool canReviewAnyDepartment,
        bool canApproveLongRequests,
        DateTime nowUtc)
    {
        var check = ValidateReview(
            reviewerId,
            reviewerDepartmentId,
            canReviewAnyDepartment,
            canApproveLongRequests);
        if (!check.IsSuccess)
        {
            return check;
        }

        Status = LeaveStatus.Approved;
        ReviewedById = reviewerId;
        ReviewedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Reject(
        Guid reviewerId,
        Guid? reviewerDepartmentId,
        bool canReviewAnyDepartment,
        bool canApproveLongRequests,
        DateTime nowUtc)
    {
        var check = ValidateReview(
            reviewerId,
            reviewerDepartmentId,
            canReviewAnyDepartment,
            canApproveLongRequests);
        if (!check.IsSuccess)
        {
            return check;
        }

        Status = LeaveStatus.Rejected;
        ReviewedById = reviewerId;
        ReviewedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Cancel(Guid requesterId)
    {
        if (EmployeeId != requesterId)
        {
            return Result.Forbidden("You can only cancel your own request.");
        }

        if (Status != LeaveStatus.Pending)
        {
            return Result.Conflict("Only pending requests can be cancelled.");
        }

        Status = LeaveStatus.Cancelled;
        return Result.Success();
    }

    private Result ValidateReview(
        Guid reviewerId,
        Guid? reviewerDepartmentId,
        bool canReviewAnyDepartment,
        bool canApproveLongRequests)
    {
        if (EmployeeId == reviewerId)
        {
            return Result.Forbidden("You cannot approve or reject your own request.");
        }

        // Company-wide reviewers (Administrator: leaves.view.all) may review
        // any department. A manager without that scope is limited to their own.
        if (!canReviewAnyDepartment
            && (reviewerDepartmentId is null || DepartmentId != reviewerDepartmentId))
        {
            return Result.Forbidden("You can only review requests from your own department.");
        }

        if (Status != LeaveStatus.Pending)
        {
            return Result.Conflict("Only pending requests can be approved or rejected.");
        }

        // ABAC rule: a regular manager may only approve short requests. HR
        // holds leaves.approve.long and can review longer absences.
        if (DurationDays > ManagerApprovalLimitDays && !canApproveLongRequests)
        {
            return Result.Forbidden(
                $"Requests longer than {ManagerApprovalLimitDays} calendar days require HR approval.");
        }

        return Result.Success();
    }
}
