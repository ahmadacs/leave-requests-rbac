using LeaveRequests.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRequests.Api.Controllers;

/// <summary>Shared helpers for translating domain <see cref="Result"/>s to problem details.</summary>
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult MapResult(Result result) => result.Status switch
    {
        ResultStatus.Forbidden => Problems.Forbidden(HttpContext, result.Message!),
        ResultStatus.NotFound => Problems.NotFound(HttpContext, result.Message!),
        ResultStatus.Conflict => Problems.Conflict(HttpContext, result.Message!),
        _ => Problems.BadRequest(HttpContext, result.Message ?? "The request could not be processed.")
    };

    protected ObjectResult Missing(string permission) =>
        Problems.Forbidden(
            HttpContext,
            $"The following permission(s) are required: {permission}.",
            new Dictionary<string, object?> { ["missingPermissions"] = new[] { permission } });
}
