namespace Contextify.Logging;

/// <summary>
/// Defines the contract for Contextify-specific logging operations.
/// Provides a unified abstraction for logging events that can be implemented
/// by custom logging providers or adapted to existing logging frameworks.
/// Implementations should be thread-safe and handle concurrent logging calls.
/// </summary>
public interface IContextifyLogging
{
    /// <summary>
    /// Logs a single event asynchronously using the configured logging infrastructure.
    /// Implementations should handle any exceptions that occur during logging
    /// to prevent logging failures from affecting application stability.
    /// </summary>
    /// <param name="evt">The log event to record. Contains timestamp, level, message, and optional context.</param>
    /// <remarks>
    /// Implementations should:
    /// - Not throw exceptions for logging failures
    /// - Respect the event's level for filtering
    /// - Include all properties from the event in structured logging
    /// - Handle null properties gracefully
    /// - Be safe for concurrent use from multiple threads
    /// </remarks>
    void Log(ContextifyLogEvent evt);

    /// <summary>
    /// Checks whether logging at the specified level is enabled.
    /// Allows callers to avoid expensive event creation when logging is disabled.
    /// </summary>
    /// <param name="level">The log level to check.</param>
    /// <returns>True if logging at the specified level is enabled; otherwise, false.</returns>
    bool IsEnabled(ContextifyLogLevel level);

    /// <summary>
    /// Logs a trace-level event for detailed debugging information.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="category">The optional category for the log entry.</param>
    void LogTrace(string message, string? category = null)
    {
        if (IsEnabled(ContextifyLogLevel.Trace))
        {
            Log(ContextifyLogEvent.Create(ContextifyLogLevel.Trace, message, category));
        }
    }

    /// <summary>
    /// Logs a debug-level event for development and troubleshooting.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="category">The optional category for the log entry.</param>
    void LogDebug(string message, string? category = null)
    {
        if (IsEnabled(ContextifyLogLevel.Debug))
        {
            Log(ContextifyLogEvent.Create(ContextifyLogLevel.Debug, message, category));
        }
    }

    /// <summary>
    /// Logs an informational-level event for normal operation.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="category">The optional category for the log entry.</param>
    void LogInformation(string message, string? category = null)
    {
        if (IsEnabled(ContextifyLogLevel.Information))
        {
            Log(ContextifyLogEvent.Create(ContextifyLogLevel.Information, message, category));
        }
    }

    /// <summary>
    /// Logs a warning-level event for potentially harmful situations.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="category">The optional category for the log entry.</param>
    void LogWarning(string message, string? category = null)
    {
        if (IsEnabled(ContextifyLogLevel.Warning))
        {
            Log(ContextifyLogEvent.Create(ContextifyLogLevel.Warning, message, category));
        }
    }

    /// <summary>
    /// Logs an error-level event for significant failures.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="exception">The optional exception that caused the error.</param>
    /// <param name="category">The optional category for the log entry.</param>
    void LogError(string message, Exception? exception = null, string? category = null)
    {
        if (IsEnabled(ContextifyLogLevel.Error))
        {
            Log(ContextifyLogEvent.Create(ContextifyLogLevel.Error, message, category, exception));
        }
    }

    /// <summary>
    /// Logs a critical-level event for severe failures requiring immediate attention.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="exception">The optional exception that caused the critical failure.</param>
    /// <param name="category">The optional category for the log entry.</param>
    void LogCritical(string message, Exception? exception = null, string? category = null)
    {
        if (IsEnabled(ContextifyLogLevel.Critical))
        {
            Log(ContextifyLogEvent.Create(ContextifyLogLevel.Critical, message, category, exception));
        }
    }
}
