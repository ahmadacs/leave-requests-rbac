using LeaveRequests.Api.Common;
using LeaveRequests.Api.Configuration;
using LeaveRequests.Api.Contracts.Auth;
using LeaveRequests.Api.Domain.Entities;
using LeaveRequests.Api.Security.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace LeaveRequests.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    private const string InvalidCredentials = "Invalid email or password.";
    private const string InvalidRefreshToken = "The refresh token is invalid or has expired.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IRefreshTokenService refreshTokens,
        IOptions<JwtOptions> jwtOptions)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _refreshTokens = refreshTokens;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // Unknown, inactive or locked-out accounts all get the SAME message.
        if (user is null || !user.IsActive || await _userManager.IsLockedOutAsync(user))
        {
            return Problems.Unauthorized(HttpContext, InvalidCredentials);
        }

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            await _userManager.AccessFailedAsync(user);
            return Problems.Unauthorized(HttpContext, InvalidCredentials);
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        return await IssueSessionAsync(user, ct);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var rawToken = Request.Cookies[_jwtOptions.RefreshCookieName];
        if (string.IsNullOrEmpty(rawToken))
        {
            return Problems.Unauthorized(HttpContext, InvalidRefreshToken);
        }

        var result = await _refreshTokens.RotateAsync(rawToken, ClientIp(), ct);
        if (!result.Succeeded || result.User is null || result.RawToken is null)
        {
            ClearRefreshCookie();
            return Problems.Unauthorized(HttpContext, InvalidRefreshToken);
        }

        var roles = await _userManager.GetRolesAsync(result.User);
        var accessToken = _tokenService.CreateAccessToken(result.User, roles);
        SetRefreshCookie(result.RawToken);

        return Ok(new AuthResponse(
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            UserProfile.From(result.User, roles)));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var rawToken = Request.Cookies[_jwtOptions.RefreshCookieName];
        if (!string.IsNullOrEmpty(rawToken))
        {
            await _refreshTokens.RevokeAsync(rawToken, ct);
        }

        ClearRefreshCookie();
        return NoContent();
    }

    private async Task<IActionResult> IssueSessionAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _tokenService.CreateAccessToken(user, roles);
        var refreshToken = await _refreshTokens.IssueAsync(user, ClientIp(), ct);
        SetRefreshCookie(refreshToken);

        return Ok(new AuthResponse(
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            UserProfile.From(user, roles)));
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private void SetRefreshCookie(string token)
    {
        Response.Cookies.Append(_jwtOptions.RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = _jwtOptions.RefreshCookiePath,
            Expires = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays),
            IsEssential = true
        });
    }

    private void ClearRefreshCookie()
    {
        Response.Cookies.Delete(_jwtOptions.RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = _jwtOptions.RefreshCookiePath
        });
    }
}
