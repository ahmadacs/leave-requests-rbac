using System.ComponentModel.DataAnnotations;
using LeaveRequests.Api.Domain.Entities;

namespace LeaveRequests.Api.Contracts.LeaveRequests;

public sealed record CreateLeaveRequestRequest : IValidatableObject
{
    [Required]
    public LeaveType Type { get; init; }

    [Required]
    public DateOnly StartDate { get; init; }

    [Required]
    public DateOnly EndDate { get; init; }

    [Required]
    [MaxLength(1000)]
    public string Reason { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate < StartDate)
        {
            yield return new ValidationResult(
                "EndDate must be on or after StartDate.",
                [nameof(EndDate)]);
        }
    }
}
