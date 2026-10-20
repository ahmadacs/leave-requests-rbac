using System.ComponentModel.DataAnnotations;

namespace LeaveRequests.Api.Contracts.Users;

public sealed record UpdateUserRolesRequest
{
    [Required]
    public IReadOnlyCollection<string> Roles { get; init; } = [];
}
