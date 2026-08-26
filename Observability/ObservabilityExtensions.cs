using Microsoft.Extensions.Logging.Console;

namespace Gym_Management.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Registers the gym console formatter (dev-friendly) or JSON console (prod),
    /// health checks, and activity source listeners are opt-in via diagnostics config.
    /// </summary>
    public static WebApplicationBuilder AddGymObservability(this WebApplicationBuilder builder)
    {
        var useJson = builder.Configuration.GetValue("Observability:JsonConsole", false)
                      || builder.Environment.IsProduction();

        builder.Logging.ClearProviders();

        if (useJson)
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
                options.UseUtcTimestamp = true;
                options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
            });
        }
        else
        {
            builder.Logging.AddConsole(options => options.FormatterName = GymConsoleFormatter.FormatterName);
            builder.Logging.AddConsoleFormatter<GymConsoleFormatter, GymConsoleFormatterOptions>(options =>
            {
                options.TimestampFormat = "HH:mm:ss.fff";
                options.UseUtcTimestamp = false;
                options.ColorEnabled = true;
                options.IncludeScopes = true;
            });
        }

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        return builder;
    }

    public static WebApplication UseGymObservability(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<RequestObservabilityMiddleware>();

        app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready")
        });

        return app;
    }
}
