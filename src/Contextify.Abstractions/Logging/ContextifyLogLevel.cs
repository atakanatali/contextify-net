namespace Contextify.Logging;

/// <summary>
/// Represents the severity level of a Contextify log event.
/// Maps to standard logging levels while providing Contextify-specific semantics.
/// Used for filtering and routing log events to appropriate sinks.
/// </summary>
public enum ContextifyLogLevel
{
    /// <summary>
    /// Detailed tracing information for debugging complex interactions.
    /// Includes method entry/exit, internal state changes, and protocol details.
    /// Typically disabled in production due to high volume.
    /// </summary>
    Trace = 0,

    /// <summary>
    /// Debug-level information for development and troubleshooting.
    /// Includes intermediate processing states and non-critical events.
    /// Useful for understanding flow during development.
    /// </summary>
    Debug = 1,

    /// <summary>
    /// General informational messages about normal operation.
    /// Includes service startup, tool registration, and successful completions.
    /// Default level for most operational logging.
    /// </summary>
    Information = 2,

    /// <summary>
    /// Warning messages for potentially harmful situations that don't prevent execution.
    /// Includes deprecated usage, retry attempts, and non-critical failures.
    /// Indicates attention may be required but system continues functioning.
    /// </summary>
    Warning = 3,

    /// <summary>
    /// Error messages for significant failures that impact functionality.
    /// Includes failed tool invocations, transport errors, and validation failures.
    /// Indicates a problem that affected the current operation.
    /// </summary>
    Error = 4,

    /// <summary>
    /// Critical messages for severe failures that require immediate attention.
    /// Includes unhandled exceptions, service crashes, and data loss scenarios.
    /// Indicates system-wide impact requiring urgent intervention.
    /// </summary>
    Critical = 5
}
