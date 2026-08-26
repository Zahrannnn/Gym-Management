using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Gym_Management.Services;

/// <summary>
/// Central writer for RFC 7807 ProblemDetails responses that always include the
/// machine-readable string field <c>reason</c> (AGENTS.md error contract).
/// Used by the exception middleware and by the JWT 401/403 events.
/// </summary>
public static class ProblemDetailsWriter
{
    public static ProblemDetails Create(int status, string reason, string title, string? detail = null, string? instance = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = instance
        };
        problem.Extensions["reason"] = reason;
        return problem;
    }

    public static async Task WriteAsync(HttpContext context, int status, string reason, string title, string? detail = null)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(
            Create(status, reason, title, detail, context.Request.Path),
            (JsonSerializerOptions?)null,
            contentType: "application/problem+json");
    }
}
