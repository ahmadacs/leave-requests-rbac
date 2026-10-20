namespace LeaveRequests.Api.Realtime;

public interface IUserNotifier
{
    Task PermissionsChangedAsync(
        IEnumerable<Guid> userIds,
        CancellationToken ct = default);
}
