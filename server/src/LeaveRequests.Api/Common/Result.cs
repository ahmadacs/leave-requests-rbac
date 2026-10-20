namespace LeaveRequests.Api.Common;

public enum ResultStatus
{
    Success,
    Validation,
    Forbidden,
    NotFound,
    Conflict
}

/// <summary>
/// Outcome of a domain operation. Business rules return this instead of
/// throwing, so controllers can translate rules into HTTP status codes.
/// </summary>
public class Result
{
    protected Result(ResultStatus status, string? message)
    {
        Status = status;
        Message = message;
    }

    public ResultStatus Status { get; }
    public string? Message { get; }
    public bool IsSuccess => Status == ResultStatus.Success;

    public static Result Success() => new(ResultStatus.Success, null);
    public static Result Validation(string message) => new(ResultStatus.Validation, message);
    public static Result Forbidden(string message) => new(ResultStatus.Forbidden, message);
    public static Result NotFound(string message) => new(ResultStatus.NotFound, message);
    public static Result Conflict(string message) => new(ResultStatus.Conflict, message);
}
