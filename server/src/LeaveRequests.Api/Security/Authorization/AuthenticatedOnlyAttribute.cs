using Microsoft.AspNetCore.Authorization;

namespace LeaveRequests.Api.Security.Authorization;

/// <summary>
/// Marks an endpoint as reachable only by a signed-in user. No permission or
/// policy is checked — this is the "somebody is authenticated" tier, used as
/// the third marker recognised by the architecture test.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuthenticatedOnlyAttribute : AuthorizeAttribute
{
}
