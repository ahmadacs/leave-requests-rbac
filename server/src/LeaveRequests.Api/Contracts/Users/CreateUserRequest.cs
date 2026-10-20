using System.ComponentModel.DataAnnotations;

namespace LeaveRequests.Api.Contracts.Users;

public sealed record CreateUserRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string FullName { get; init; } = string.Empty;

    public Guid? DepartmentId { get; init; }

    /// <summary>Optional. Falls back to the shared demo password.</summary>
    public string? Password { get; init; }

    public IReadOnlyCollection<string>? Roles { get; init; }
}
