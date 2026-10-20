namespace LeaveRequests.Api.Configuration;

/// <summary>
/// JWT / session settings. <see cref="SigningKey"/> is supplied through user
/// secrets (or the <c>Jwt__SigningKey</c> environment variable) and is never
/// committed to source control.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "LeaveRequestsApi";
    public string Audience { get; set; } = "LeaveRequestsClient";
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;

    public string RefreshCookieName { get; set; } = "refresh_token";

    /// <summary>The refresh cookie is only sent to the auth endpoints.</summary>
    public string RefreshCookiePath { get; set; } = "/api/auth";
}
