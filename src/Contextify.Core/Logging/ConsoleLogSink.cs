using Contextify.Logging;
using System.Diagnostics;

namespace Contextify.Core.Logging;

/// <summary>
/// Fallback sink that writes Contextify log events to the system console.
/// Used when no other logging infrastructure is configured. Outputs to Console.Error
/// for warnings and errors, and Console.Out for informational and lower levels.
/// Includes color coding for visual clarity and thread-safe console access.
/// Does not require DI registration and never throws exceptions.
/// </summary>
internal sealed class ConsoleLogSink : IContextifyLogSink
{
    private readonly ContextifyLogLevel _minimumLevel;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the ConsoleLogSink class.
    /// </summary>
    /// <param name="minimumLevel">The minimum log level to output. Defaults to Information.</param>
    public ConsoleLogSink(ContextifyLogLevel minimumLevel = ContextifyLogLevel.Information)
    {
        _minimumLevel = minimumLevel;
    }

    /// <summary>
    /// Writes a log event to the console with appropriate color coding.
    /// Thread-safe to handle concurrent logging from multiple threads.
    /// Silently handles any exceptions during console output.
    /// </summary>
    /// <param name="evt">The log event to write.</param>
    public void Write(ContextifyLogEvent evt)
    {
        if (evt is null)
        {
            return;
        }

        if (!IsEnabled(evt.Level))
        {
            return;
        }

        try
        {
            lock (_lock)
            {
                var originalColor = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = GetForegroundColor(evt.Level);
                    var output = evt.Level >= ContextifyLogLevel.Warning ? Console.Error : Console.Out;
                    output.WriteLine(FormatMessage(evt));
                }
                finally
                {
                    Console.ForegroundColor = originalColor;
                }
            }
        }
        catch
        {
            // Silently ignore console output failures
        }
    }

    /// <summary>
    /// Checks whether logging at the specified level is enabled.
    /// </summary>
    /// <param name="level">The log level to check.</param>
    /// <returns>True if logging at the specified level is enabled; otherwise, false.</returns>
    public bool IsEnabled(ContextifyLogLevel level)
    {
        return level >= _minimumLevel;
    }

    /// <summary>
    /// Formats a log event for console output with structured properties.
    /// </summary>
    /// <param name="evt">The log event to format.</param>
    /// <returns>A formatted string for console output.</returns>
    private static string FormatMessage(ContextifyLogEvent evt)
    {
        var parts = new List<string>
        {
            $"[{evt.Timestamp:yyyy-MM-dd HH:mm:ss.fff}]",
            $"[{evt.Level}]"
        };

        if (!string.IsNullOrEmpty(evt.Category))
        {
            parts.Add($"[{evt.Category}]");
        }

        parts.Add(evt.Message);

        if (evt.Exception is not null)
        {
            parts.Add($"| Exception: {evt.Exception.Message}");
            if (evt.Exception.StackTrace is not null)
            {
                parts.Add($"  Stack: {evt.Exception.StackTrace}");
            }
        }

        if (evt.Properties is not null && evt.Properties.Count > 0)
        {
            var props = string.Join(", ", evt.Properties
                .Where(kvp => kvp.Value is not null)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));
            if (!string.IsNullOrEmpty(props))
            {
                parts.Add($"| Properties: {props}");
            }
        }

        if (evt.TraceId is not null)
        {
            parts.Add($"| TraceId: {evt.TraceId}");
        }

        if (evt.SpanId is not null)
        {
            parts.Add($"| SpanId: {evt.SpanId}");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Gets the console foreground color for a given log level.
    /// </summary>
    /// <param name="level">The log level.</param>
    /// <returns>The console color for the log level.</returns>
    private static ConsoleColor GetForegroundColor(ContextifyLogLevel level)
    {
        return level switch
        {
            ContextifyLogLevel.Trace => ConsoleColor.DarkGray,
            ContextifyLogLevel.Debug => ConsoleColor.Gray,
            ContextifyLogLevel.Information => ConsoleColor.White,
            ContextifyLogLevel.Warning => ConsoleColor.Yellow,
            ContextifyLogLevel.Error => ConsoleColor.Red,
            ContextifyLogLevel.Critical => ConsoleColor.Magenta,
            _ => ConsoleColor.White
        };
    }
}
