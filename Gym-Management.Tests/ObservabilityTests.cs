using Gym_Management.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Gym_Management.Tests;

public class ObservabilityTests
{
    [Fact]
    public void SanitizePath_Redacts_Public_Status_Token_Segment()
    {
        var sanitized = RequestObservabilityMiddleware.SanitizePath(
            new PathString("/api/public/status/abcSecretToken"),
            new RouteValueDictionary());

        Assert.Equal("/api/public/status/***", sanitized);
    }

    [Fact]
    public void SanitizePath_Redacts_Route_Token_Value()
    {
        var route = new RouteValueDictionary { ["token"] = "raw-token-value" };
        var sanitized = RequestObservabilityMiddleware.SanitizePath(
            new PathString("/api/public/status/raw-token-value"),
            route);

        Assert.DoesNotContain("raw-token-value", sanitized);
        Assert.Contains("***", sanitized);
    }
}
