using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LeaveRequests.Api.Realtime;

[Authorize]
public sealed class NotificationsHub : Hub;
