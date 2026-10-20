namespace LeaveRequests.Api.Contracts.Auth;

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    UserProfile User);
