namespace Gym_Management.Observability;

/// <summary>
/// Assigns/propagates a correlation id on every request (header <c>X-Correlation-ID</c>),
/// pushes it into the logging scope and response headers for end-to-end tracing.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";
    public const string ScopeKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
                            && !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString().Trim()
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object?> { [ScopeKey] = correlationId }))
        {
            await next(context);
        }
    }

    public static string? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value?.ToString() : null;
}
