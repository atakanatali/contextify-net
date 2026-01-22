using System.Diagnostics;

namespace Contextify.Logging;

/// <summary>
/// Represents a structured log event in the Contextify logging system.
/// Encapsulates all contextual information for a single log entry including
/// severity, timestamp, message, exception details, and structured properties.
/// Designed as a record for immutability and value-based equality semantics.
/// </summary>
/// <param name="Timestamp">The UTC timestamp when the log event was created.</param>
/// <param name="Level">The severity level of the log event for filtering and routing.</param>
/// <param name="Message">The human-readable log message describing the event.</param>
/// <param name="Category">The logical category/source of the log event (e.g., tool name, transport type).</param>
/// <param name="Exception">The exception associated with the event, if any.</param>
/// <param name="Properties">Additional structured properties for contextual logging (e.g., RequestId, ToolName).</param>
/// <param name="EventId">The optional identifier for the specific event type.</param>
public sealed record ContextifyLogEvent(
    DateTimeOffset Timestamp,
    ContextifyLogLevel Level,
    string Message,
    string? Category = null,
    Exception? Exception = null,
    IReadOnlyDictionary<string, object?>? Properties = null,
    int? EventId = null
)
{
    /// <summary>
    /// Gets the activity trace ID if distributed tracing is enabled.
    /// Returns null if no active activity or trace ID is available.
    /// </summary>
    public string? TraceId => Activity.Current?.TraceId.ToString();

    /// <summary>
    /// Gets the activity span ID if distributed tracing is enabled.
    /// Returns null if no active activity or span ID is available.
    /// </summary>
    public string? SpanId => Activity.Current?.SpanId.ToString();

    /// <summary>
    /// Creates a new log event with the current UTC timestamp.
    /// Factory method for simpler event creation without specifying timestamp.
    /// </summary>
    /// <param name="level">The severity level of the log event.</param>
    /// <param name="message">The human-readable log message.</param>
    /// <param name="category">The logical category/source of the event.</param>
    /// <param name="exception">The exception associated with the event, if any.</param>
    /// <param name="properties">Additional structured properties for contextual logging.</param>
    /// <param name="eventId">The optional identifier for the specific event type.</param>
    /// <returns>A new ContextifyLogEvent with timestamp set to UTC now.</returns>
    public static ContextifyLogEvent Create(
        ContextifyLogLevel level,
        string message,
        string? category = null,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null,
        int? eventId = null)
    {
        return new ContextifyLogEvent(
            DateTimeOffset.UtcNow,
            level,
            message,
            category,
            exception,
            properties,
            eventId);
    }

    /// <summary>
    /// Creates a new log event with additional properties merged into existing properties.
    /// Useful for adding contextual information without modifying the original event.
    /// </summary>
    /// <param name="additionalProperties">The properties to add to the event.</param>
    /// <returns>A new ContextifyLogEvent with merged properties.</returns>
    public ContextifyLogEvent WithProperties(IDictionary<string, object?> additionalProperties)
    {
        var mergedProperties = Properties is null
            ? new Dictionary<string, object?>(additionalProperties)
            : new Dictionary<string, object?>(Properties);

        foreach (var kvp in additionalProperties)
        {
            mergedProperties[kvp.Key] = kvp.Value;
        }

        return this with { Properties = mergedProperties };
    }

    /// <summary>
    /// Returns a formatted string representation of the log event.
    /// Includes timestamp, level, category, and message in a structured format.
    /// </summary>
    /// <returns>A formatted string representation of the log event.</returns>
    public override string ToString()
    {
        var categoryPrefix = string.IsNullOrEmpty(Category) ? "" : $"[{Category}] ";
        var exceptionSuffix = Exception is null ? "" : $" | Exception: {Exception.Message}";
        return $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {categoryPrefix}{Message}{exceptionSuffix}";
    }
}
