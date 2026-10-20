using System.ComponentModel.DataAnnotations;

namespace LeaveRequests.Api.Contracts.Roles;

public sealed record CreateRoleRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; init; }

    public IReadOnlyCollection<string>? Permissions { get; init; }
}
