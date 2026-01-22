using Contextify.Logging;

namespace Contextify.Core.Logging;

/// <summary>
/// Internal sink abstraction for writing Contextify log events.
/// Provides a unified interface for different logging backends including
/// custom IContextifyLogging implementations, Microsoft.Extensions.Logging.ILogger,
/// and fallback console output. Implementations must be thread-safe.
/// </summary>
internal interface IContextifyLogSink
{
    /// <summary>
    /// Writes a log event to the configured sink.
    /// Must be thread-safe and handle concurrent calls from multiple threads.
    /// Should not throw exceptions; logging failures should be silently ignored.
    /// </summary>
    /// <param name="evt">The log event to write.</param>
    void Write(ContextifyLogEvent evt);

    /// <summary>
    /// Checks whether the sink is enabled for the specified log level.
    /// Allows callers to avoid creating log events that would be discarded.
    /// </summary>
    /// <param name="level">The log level to check.</param>
    /// <returns>True if logging at the specified level is enabled; otherwise, false.</returns>
    bool IsEnabled(ContextifyLogLevel level);
}
