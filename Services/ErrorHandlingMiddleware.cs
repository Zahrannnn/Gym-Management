using Gym_Management.Observability;

namespace Gym_Management.Services;

/// <summary>
/// Global exception handler. Maps <see cref="ApiException"/> to its HTTP status
/// with a ProblemDetails body carrying the machine-readable <c>reason</c> field,
/// and turns any unhandled exception into a generic 500 ProblemDetails.
/// </summary>
public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException ex)
        {
            var path = RequestObservabilityMiddleware.SanitizePath(context.Request.Path, context.Request.RouteValues);
            if (ex.StatusCode >= 500)
            {
                logger.LogError(
                    GymLogEvents.ApiException,
                    ex,
                    "API exception status={StatusCode} reason={Reason} {Method} {Path}",
                    ex.StatusCode,
                    ex.Reason,
                    context.Request.Method,
                    path);
            }
            else
            {
                logger.LogWarning(
                    GymLogEvents.ApiException,
                    "API exception status={StatusCode} reason={Reason} {Method} {Path}",
                    ex.StatusCode,
                    ex.Reason,
                    context.Request.Method,
                    path);
            }

            await ProblemDetailsWriter.WriteAsync(context, ex.StatusCode, ex.Reason, ReasonPhrase(ex.StatusCode), ex.Message);
        }
        catch (Exception ex)
        {
            var path = RequestObservabilityMiddleware.SanitizePath(context.Request.Path, context.Request.RouteValues);
            logger.LogError(
                GymLogEvents.UnhandledException,
                ex,
                "Unhandled exception while processing {Method} {Path}",
                context.Request.Method,
                path);
            await ProblemDetailsWriter.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "An unexpected error occurred.",
                "The error has been logged.");
        }
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch
    {
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Validation Failed",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        _ => "Error"
    };
}
