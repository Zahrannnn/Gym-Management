using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Gym_Management.Observability;

public sealed class GymConsoleFormatterOptions : ConsoleFormatterOptions
{
    /// <summary>Include ANSI colors when the console supports them.</summary>
    public bool ColorEnabled { get; set; } = true;
}

/// <summary>
/// Single-line, colorized console formatter with timestamp, level, category,
/// correlation id (from scope), and structured message. No third-party packages.
/// </summary>
public sealed class GymConsoleFormatter(IOptionsMonitor<GymConsoleFormatterOptions> options)
    : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "gym";

    private readonly IOptionsMonitor<GymConsoleFormatterOptions> _options = options;

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var opts = _options.CurrentValue;
        var timestamp = opts.TimestampFormat is { Length: > 0 }
            ? (opts.UseUtcTimestamp ? DateTime.UtcNow : DateTime.Now).ToString(opts.TimestampFormat)
            : (opts.UseUtcTimestamp ? DateTime.UtcNow : DateTime.Now).ToString("HH:mm:ss.fff");

        var level = LevelLabel(logEntry.LogLevel);
        var color = opts.ColorEnabled;
        var category = ShortCategory(logEntry.Category);

        if (color)
        {
            textWriter.Write(LevelAnsi(logEntry.LogLevel));
        }

        textWriter.Write(timestamp);
        textWriter.Write(' ');
        textWriter.Write(level);
        if (color)
        {
            textWriter.Write(AnsiReset);
        }

        textWriter.Write(' ');
        textWriter.Write(category);

        var correlationId = TryGetCorrelationId(scopeProvider);
        if (correlationId is not null)
        {
            textWriter.Write(" [");
            if (color)
            {
                textWriter.Write(AnsiCyan);
            }

            textWriter.Write(correlationId);
            if (color)
            {
                textWriter.Write(AnsiReset);
            }

            textWriter.Write(']');
        }

        textWriter.Write(" | ");
        textWriter.Write(logEntry.Formatter(logEntry.State, logEntry.Exception));

        if (logEntry.Exception is not null)
        {
            textWriter.Write(" :: ");
            textWriter.Write(logEntry.Exception.GetType().Name);
            textWriter.Write(": ");
            textWriter.Write(logEntry.Exception.Message);
        }

        textWriter.WriteLine();
    }

    private static string? TryGetCorrelationId(IExternalScopeProvider? scopeProvider)
    {
        string? found = null;
        scopeProvider?.ForEachScope((scope, _) =>
        {
            if (found is not null)
            {
                return;
            }

            if (scope is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                foreach (var (key, value) in kvps)
                {
                    if (key is CorrelationIdMiddleware.ScopeKey or "CorrelationId" && value is not null)
                    {
                        found = value.ToString();
                        return;
                    }
                }
            }
        }, (object?)null);

        return found;
    }

    private static string ShortCategory(string category)
    {
        var lastDot = category.LastIndexOf('.');
        return lastDot >= 0 && lastDot < category.Length - 1
            ? category[(lastDot + 1)..]
            : category;
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };

    private const string AnsiReset = "\x1b[0m";
    private const string AnsiCyan = "\x1b[36m";

    private static string LevelAnsi(LogLevel level) => level switch
    {
        LogLevel.Trace => "\x1b[90m",
        LogLevel.Debug => "\x1b[37m",
        LogLevel.Information => "\x1b[32m",
        LogLevel.Warning => "\x1b[33m",
        LogLevel.Error => "\x1b[31m",
        LogLevel.Critical => "\x1b[97;41m",
        _ => AnsiReset
    };
}
