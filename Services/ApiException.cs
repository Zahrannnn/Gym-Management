namespace Gym_Management.Services;

using Gym_Management.Validation;

/// <summary>
/// Domain/API exception carrying the RFC 7807 status and the machine-readable
/// <c>reason</c> string from the AGENTS.md error contract. Thrown by controllers
/// and services; rendered by <see cref="ErrorHandlingMiddleware"/>.
/// </summary>
public class ApiException(int statusCode, string reason, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Reason { get; } = reason;
}

public static class ApiErrors
{
    public static ApiException Unauthorized(string detail = "Authentication failed.") =>
        new(StatusCodes.Status401Unauthorized, ErrorReasons.Unauthorized, detail);

    public static ApiException Forbidden(string detail = "You do not have permission to perform this action.") =>
        new(StatusCodes.Status403Forbidden, ErrorReasons.Forbidden, detail);

    public static ApiException NotFound(string detail = "The requested resource was not found.") =>
        new(StatusCodes.Status404NotFound, ErrorReasons.NotFound, detail);

    public static ApiException OverlapConflict(string detail) =>
        new(StatusCodes.Status409Conflict, ErrorReasons.OverlapConflict, detail);

    public static ApiException Validation(string detail) =>
        new(StatusCodes.Status422UnprocessableEntity, ErrorReasons.Validation, detail);
}
