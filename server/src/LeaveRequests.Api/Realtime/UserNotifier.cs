using Microsoft.AspNetCore.SignalR;

namespace LeaveRequests.Api.Realtime;

public sealed class UserNotifier : IUserNotifier
{
    private readonly IHubContext<NotificationsHub> _hub;

    public UserNotifier(IHubContext<NotificationsHub> hub) => _hub = hub;

    public Task PermissionsChangedAsync(
        IEnumerable<Guid> userIds,
        CancellationToken ct = default)
    {
        var targets = userIds
            .Distinct()
            .Select(id => id.ToString())
            .ToArray();

        return targets.Length == 0
            ? Task.CompletedTask
            : _hub.Clients.Users(targets).SendCoreAsync("PermissionsChanged", [], ct);
    }
}
