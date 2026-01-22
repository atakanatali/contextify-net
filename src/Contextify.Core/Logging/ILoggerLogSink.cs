using Contextify.Logging;
using Microsoft.Extensions.Logging;

namespace Contextify.Core.Logging;

/// <summary>
/// Adapter sink that bridges Contextify logging to Microsoft.Extensions.Logging.ILogger.
/// Converts ContextifyLogEvent instances to ILogger calls with proper category mapping
/// and structured property support. Does not require DI registration and handles missing
/// ILoggerFactory gracefully.
/// </summary>
internal sealed class ILoggerLogSink : IContextifyLogSink
{
    private readonly ILogger _logger;
    private readonly ContextifyLogLevel _minimumLevel;

    /// <summary>
    /// Initializes a new instance of the ILoggerLogSink class.
    /// </summary>
    /// <param name="logger">The Microsoft.Extensions.Logging.ILogger to write to.</param>
    /// <param name="minimumLevel">The minimum log level to output.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public ILoggerLogSink(ILogger logger, ContextifyLogLevel minimumLevel = ContextifyLogLevel.Information)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _minimumLevel = minimumLevel;
    }

    /// <summary>
    /// Writes a Contextify log event to the underlying ILogger.
    /// Maps ContextifyLogLevel to LogLevel and includes structured properties.
    /// Silently handles any exceptions during logging.
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
            var logLevel = MapLogLevel(evt.Level);
            if (logLevel is null)
            {
                return;
            }

            // Build state object for structured logging
            var state = new Dictionary<string, object?>
            {
                ["Message"] = evt.Message
            };

            if (!string.IsNullOrEmpty(evt.Category))
            {
                state["Category"] = evt.Category;
            }

            // Add event properties
            if (evt.Properties is not null)
            {
                foreach (var kvp in evt.Properties)
                {
                    state[$"Prop_{kvp.Key}"] = kvp.Value;
                }
            }

            // Add distributed tracing info
            if (evt.TraceId is not null)
            {
                state["TraceId"] = evt.TraceId;
            }

            if (evt.SpanId is not null)
            {
                state["SpanId"] = evt.SpanId;
            }

            // Log with structured properties
            if (evt.Exception is not null)
            {
                _logger.Log(logLevel.Value, evt.Exception, "{Message}", evt.Message);
            }
            else
            {
                _logger.Log(logLevel.Value, "{Message}", evt.Message);
            }

            // Begin scope for structured properties
            if (state.Count > 1)
            {
                var scope = _logger.BeginScope(state);
                if (scope is IDisposable disposableScope)
                {
                    disposableScope.Dispose();
                }
            }
        }
        catch
        {
            // Silently ignore logging failures to prevent cascading errors
        }
    }

    /// <summary>
    /// Checks whether logging at the specified level is enabled.
    /// </summary>
    /// <param name="level">The log level to check.</param>
    /// <returns>True if logging at the specified level is enabled; otherwise, false.</returns>
    public bool IsEnabled(ContextifyLogLevel level)
    {
        if (level < _minimumLevel)
        {
            return false;
        }

        var logLevel = MapLogLevel(level);
        return logLevel is not null && _logger.IsEnabled(logLevel.Value);
    }

    /// <summary>
    /// Maps ContextifyLogLevel to Microsoft.Extensions.Logging.LogLevel.
    /// Returns null for log levels that should be filtered.
    /// </summary>
    /// <param name="level">The Contextify log level to map.</param>
    /// <returns>The corresponding LogLevel or null if unmapped.</returns>
    private static LogLevel? MapLogLevel(ContextifyLogLevel level)
    {
        return level switch
        {
            ContextifyLogLevel.Trace => LogLevel.Trace,
            ContextifyLogLevel.Debug => LogLevel.Debug,
            ContextifyLogLevel.Information => LogLevel.Information,
            ContextifyLogLevel.Warning => LogLevel.Warning,
            ContextifyLogLevel.Error => LogLevel.Error,
            ContextifyLogLevel.Critical => LogLevel.Critical,
            _ => null
        };
    }
}
