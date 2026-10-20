using LeaveRequests.Api.Security;
using Microsoft.AspNetCore.SignalR;

namespace LeaveRequests.Api.Realtime;

public sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(ClaimNames.Subject)?.Value;
}
