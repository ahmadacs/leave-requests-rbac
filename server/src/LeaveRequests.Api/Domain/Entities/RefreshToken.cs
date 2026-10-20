using System.ComponentModel.DataAnnotations.Schema;

namespace LeaveRequests.Api.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByHash { get; set; }
    public string? CreatedByIp { get; set; }

    public ApplicationUser User { get; set; } = null!;

    [NotMapped]
    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;
}
