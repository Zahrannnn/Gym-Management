using System.Diagnostics;

namespace Gym_Management.Observability;

/// <summary>
/// Emits structured request start/complete logs with duration. Redacts QR tokens from
/// public status paths so plaintext tokens never appear in logs.
/// </summary>
public sealed class RequestObservabilityMiddleware(RequestDelegate next, ILogger<RequestObservabilityMiddleware> logger)
{
    private static readonly PathString HealthPath = new("/health");
    private static readonly PathString ReadyPath = new("/health/ready");

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldSkip(context.Request.Path))
        {
            await next(context);
            return;
        }

        var path = SanitizePath(context.Request.Path, context.Request.RouteValues);
        var method = context.Request.Method;
        var sw = Stopwatch.StartNew();

        logger.LogInformation(
            GymLogEvents.RequestStarted,
            "HTTP {Method} {Path} started",
            method,
            path);

        try
        {
            await next(context);
            sw.Stop();

            var status = context.Response.StatusCode;
            if (status >= 500)
            {
                logger.LogError(
                    GymLogEvents.RequestFailed,
                    "HTTP {Method} {Path} completed {StatusCode} in {ElapsedMs}ms",
                    method,
                    path,
                    status,
                    sw.ElapsedMilliseconds);
            }
            else if (status >= 400)
            {
                logger.LogWarning(
                    GymLogEvents.RequestCompleted,
                    "HTTP {Method} {Path} completed {StatusCode} in {ElapsedMs}ms",
                    method,
                    path,
                    status,
                    sw.ElapsedMilliseconds);
            }
            else
            {
                logger.LogInformation(
                    GymLogEvents.RequestCompleted,
                    "HTTP {Method} {Path} completed {StatusCode} in {ElapsedMs}ms",
                    method,
                    path,
                    status,
                    sw.ElapsedMilliseconds);
            }
        }
        catch (Exception)
        {
            sw.Stop();
            logger.LogError(
                GymLogEvents.RequestFailed,
                "HTTP {Method} {Path} failed after {ElapsedMs}ms",
                method,
                path,
                sw.ElapsedMilliseconds);
            throw;
        }
    }

    private static bool ShouldSkip(PathString path) =>
        path.StartsWithSegments(HealthPath) ||
        path.StartsWithSegments(ReadyPath) ||
        path.StartsWithSegments("/swagger");

    /// <summary>Never log raw QR tokens from /api/public/status/{token}.</summary>
    public static string SanitizePath(PathString path, RouteValueDictionary routeValues)
    {
        if (routeValues.TryGetValue("token", out var token) && token is not null)
        {
            var raw = path.Value ?? path.ToString();
            var tokenText = token.ToString();
            if (!string.IsNullOrEmpty(tokenText) && raw.Contains(tokenText, StringComparison.Ordinal))
            {
                return raw.Replace(tokenText, "***", StringComparison.Ordinal);
            }
        }

        // Fallback before routing: redact last segment of public status URLs.
        var value = path.Value ?? string.Empty;
        const string publicPrefix = "/api/public/status/";
        if (value.StartsWith(publicPrefix, StringComparison.OrdinalIgnoreCase)
            && value.Length > publicPrefix.Length)
        {
            return publicPrefix + "***";
        }

        return value;
    }
}
