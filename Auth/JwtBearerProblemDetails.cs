using Gym_Management.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Gym_Management.Auth;

/// <summary>
/// JwtBearer event handlers that render 401/403 as ProblemDetails with the
/// machine-readable reason field, matching the rest of the error contract
/// (default JwtBearer responses have empty bodies).
/// </summary>
public static class JwtBearerProblemDetails
{
    public static Task OnChallenge(JwtBearerChallengeContext context)
    {
        // Suppress the default empty-body challenge; never leak why the token failed.
        context.HandleResponse();
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return ProblemDetailsWriter.WriteAsync(
            context.HttpContext,
            StatusCodes.Status401Unauthorized,
            "unauthorized",
            "Unauthorized",
            "A valid bearer token is required.");
    }

    public static Task OnForbidden(ForbiddenContext context) =>
        ProblemDetailsWriter.WriteAsync(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            "forbidden",
            "Forbidden",
            "Your role does not allow this action.");
}
