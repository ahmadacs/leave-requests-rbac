using Microsoft.AspNetCore.Mvc;

namespace LeaveRequests.Api.Security.Authorization;

/// <summary>
/// Requires the caller to hold every listed permission. Implemented with a
/// <see cref="TypeFilterAttribute"/> + <see cref="PermissionAuthorizationFilter"/>
/// (an <c>IAsyncAuthorizationFilter</c>), deliberately NOT policy-based
/// authorization.
/// <para>
/// Declaring the attribute twice on one action means AND: all permissions from
/// every attribute instance must be present.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HasPermissionAttribute : TypeFilterAttribute
{
    public HasPermissionAttribute(params string[] permissions)
        : base(typeof(PermissionAuthorizationFilter))
    {
        if (permissions is null || permissions.Length == 0)
        {
            throw new ArgumentException("At least one permission is required.", nameof(permissions));
        }

        Arguments = [permissions];
    }
}
