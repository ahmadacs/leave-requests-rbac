using Microsoft.AspNetCore.Mvc;

namespace LeaveRequests.Api.Common;

/// <summary>
/// Helpers for returning RFC 9457 problem details with the correct content
/// type and a consistent shape.
/// </summary>
public static class Problems
{
    public const string ContentType = "application/problem+json";

    public static ObjectResult Create(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { ContentType }
        };
    }

    public static ObjectResult Unauthorized(HttpContext httpContext, string detail) =>
        Create(httpContext, StatusCodes.Status401Unauthorized, "Unauthorized", detail);

    public static ObjectResult Forbidden(
        HttpContext httpContext,
        string detail,
        IDictionary<string, object?>? extensions = null) =>
        Create(httpContext, StatusCodes.Status403Forbidden, "Forbidden", detail, extensions);

    public static ObjectResult NotFound(HttpContext httpContext, string detail) =>
        Create(httpContext, StatusCodes.Status404NotFound, "Not Found", detail);

    public static ObjectResult Conflict(HttpContext httpContext, string detail) =>
        Create(httpContext, StatusCodes.Status409Conflict, "Conflict", detail);

    public static ObjectResult BadRequest(HttpContext httpContext, string detail) =>
        Create(httpContext, StatusCodes.Status400BadRequest, "Bad Request", detail);
}
