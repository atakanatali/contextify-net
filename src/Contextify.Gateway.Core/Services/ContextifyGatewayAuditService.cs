using System.Diagnostics;
using Contextify.Logging;
using Microsoft.Extensions.Logging;

namespace Contextify.Gateway.Core.Services;

/// <summary>
/// Service for auditing tool invocation calls in the Contextify Gateway.
/// Provides structured audit logging for tool call lifecycle events including
/// start, completion, and failure with correlation ID propagation.
/// Designed for high-concurrency scenarios with millions of concurrent requests.
/// Thread-safe and optimized for minimal overhead during request processing.
/// </summary>
public sealed class ContextifyGatewayAuditService
{
    /// <summary>
    /// HTTP header name for correlation ID propagation.
    /// This header is forwarded to upstream services for distributed tracing.
    /// </summary>
    public const string CorrelationIdHeaderName = "X-Correlation-Id";

    private readonly ILogger<ContextifyGatewayAuditService> _logger;
    private readonly IContextifyLogging? _contextifyLogging;

    /// <summary>
    /// Initializes a new instance with required logging dependencies.
    /// Supports both ILogger and optional IContextifyLogging for dual audit output.
    /// </summary>
    /// <param name="logger">The Microsoft ILogger for structured diagnostic logging.</param>
    /// <param name="contextifyLogging">Optional Contextify logging for structured event emission.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public ContextifyGatewayAuditService(
        ILogger<ContextifyGatewayAuditService> logger,
        IContextifyLogging? contextifyLogging = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contextifyLogging = contextifyLogging;
    }

    /// <summary>
    /// Generates or retrieves a correlation ID for the current request.
    /// Creates a new GUID if no correlation ID exists in the HTTP context.
    /// Correlation IDs are used to trace requests across service boundaries.
    /// </summary>
    /// <param name="existingCorrelationId">Existing correlation ID from HTTP headers, if present.</param>
    /// <returns>A valid GUID correlation ID for request tracing.</returns>
    public static Guid GenerateOrGetCorrelationId(string? existingCorrelationId)
    {
        if (Guid.TryParse(existingCorrelationId, out var correlationId))
        {
            return correlationId;
        }

        return Guid.NewGuid();
    }

    /// <summary>
    /// Extracts the correlation ID from HTTP request headers.
    /// Returns null if the header is not present or contains an invalid value.
    /// </summary>
    /// <param name="headers">The HTTP request headers dictionary.</param>
    /// <returns>The correlation ID if present and valid; otherwise, null.</returns>
    public static string? ExtractCorrelationIdFromHeaders(IDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return null;
        }

        if (headers.TryGetValue(CorrelationIdHeaderName, out var correlationId) &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId;
        }

        return null;
    }

    /// <summary>
    /// Formats the correlation ID as a string header value.
    /// Converts the GUID to its standard string representation for HTTP transmission.
    /// </summary>
    /// <param name="correlationId">The correlation ID to format.</param>
    /// <returns>The string representation of the correlation ID.</returns>
    public static string FormatCorrelationIdHeader(Guid correlationId)
    {
        return correlationId.ToString();
    }

    /// <summary>
    /// Records the start of a tool invocation for audit purposes.
    /// Logs the invocation details with structured properties for analytics.
    /// Sensitive argument data is not logged by default to prevent information leakage.
    /// </summary>
    /// <param name="invocationId">Unique identifier for this tool invocation.</param>
    /// <param name="externalToolName">The external tool name as exposed by the gateway.</param>
    /// <param name="upstreamName">The name of the upstream server handling the tool.</param>
    /// <param name="correlationId">The correlation ID for distributed tracing.</param>
    /// <param name="argumentsSizeBytes">Optional size of arguments in bytes (for volume tracking).</param>
    /// <param name="argumentsHash">Optional hash of arguments content (for change detection).</param>
    /// <remarks>
    /// Audit events are emitted to both ILogger and IContextifyLogging (if registered).
    /// The event includes trace/span IDs from the current Activity for distributed tracing.
    /// </remarks>
    public void AuditToolCallStart(
        Guid invocationId,
        string externalToolName,
        string upstreamName,
        Guid correlationId,
        int? argumentsSizeBytes = null,
        string? argumentsHash = null)
    {
        if (string.IsNullOrWhiteSpace(externalToolName))
        {
            throw new ArgumentException("External tool name cannot be null or whitespace.", nameof(externalToolName));
        }

        if (string.IsNullOrWhiteSpace(upstreamName))
        {
            throw new ArgumentException("Upstream name cannot be null or whitespace.", nameof(upstreamName));
        }

        var properties = CreateAuditProperties(
            invocationId,
            externalToolName,
            upstreamName,
            correlationId,
            argumentsSizeBytes,
            argumentsHash);

        _logger.LogDebug(
            "Tool call started: InvocationId={InvocationId}, Tool={ToolName}, Upstream={UpstreamName}, CorrelationId={CorrelationId}",
            invocationId,
            externalToolName,
            upstreamName,
            correlationId);

        EmitContextifyLogEvent(
            ContextifyLogLevel.Information,
            "Gateway tool call started",
            properties,
            eventId: 1001);
    }

    /// <summary>
    /// Records the completion of a tool invocation for audit purposes.
    /// Logs the final status, duration, and success/failure indication.
    /// Used for analytics, performance monitoring, and compliance auditing.
    /// </summary>
    /// <param name="invocationId">Unique identifier for this tool invocation.</param>
    /// <param name="externalToolName">The external tool name as exposed by the gateway.</param>
    /// <param name="upstreamName">The name of the upstream server that handled the tool.</param>
    /// <param name="correlationId">The correlation ID for distributed tracing.</param>
    /// <param name="success">True if the tool call succeeded; false if it failed.</param>
    /// <param name="durationMs">Duration of the tool call in milliseconds.</param>
    /// <param name="errorType">Optional error type if the call failed.</param>
    /// <param name="errorMessage">Optional error message if the call failed.</param>
    /// <remarks>
    /// Audit events are emitted to both ILogger and IContextifyLogging (if registered).
    /// Duration is measured from AuditToolCallStart to this method call.
    /// </remarks>
    public void AuditToolCallEnd(
        Guid invocationId,
        string externalToolName,
        string upstreamName,
        Guid correlationId,
        bool success,
        long durationMs,
        string? errorType = null,
        string? errorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(externalToolName))
        {
            throw new ArgumentException("External tool name cannot be null or whitespace.", nameof(externalToolName));
        }

        if (string.IsNullOrWhiteSpace(upstreamName))
        {
            throw new ArgumentException("Upstream name cannot be null or whitespace.", nameof(upstreamName));
        }

        var properties = CreateAuditProperties(
            invocationId,
            externalToolName,
            upstreamName,
            correlationId,
            null,
            null,
            success,
            durationMs,
            errorType,
            errorMessage);

        var statusText = success ? "succeeded" : "failed";
        var logLevel = success ? ContextifyLogLevel.Information : ContextifyLogLevel.Warning;

        _logger.LogDebug(
            "Tool call {Status}: InvocationId={InvocationId}, Tool={ToolName}, Upstream={UpstreamName}, CorrelationId={CorrelationId}, Duration={Duration}ms",
            statusText,
            invocationId,
            externalToolName,
            upstreamName,
            correlationId,
            durationMs);

        if (!success)
        {
            _logger.LogWarning(
                "Tool call failed: InvocationId={InvocationId}, Tool={ToolName}, ErrorType={ErrorType}, Error={Error}",
                invocationId,
                externalToolName,
                errorType ?? "Unknown",
                errorMessage ?? "No error message");
        }

        EmitContextifyLogEvent(
            logLevel,
            $"Gateway tool call {statusText}",
            properties,
            eventId: success ? 1002 : 1003);
    }

    /// <summary>
    /// Creates a properties dictionary for structured audit logging.
    /// Consolidates audit-related properties for consistent log formatting.
    /// </summary>
    /// <param name="invocationId">Unique identifier for this tool invocation.</param>
    /// <param name="externalToolName">The external tool name as exposed by the gateway.</param>
    /// <param name="upstreamName">The name of the upstream server.</param>
    /// <param name="correlationId">The correlation ID for distributed tracing.</param>
    /// <param name="argumentsSizeBytes">Optional size of arguments in bytes.</param>
    /// <param name="argumentsHash">Optional hash of arguments content.</param>
    /// <param name="success">True if the call succeeded (only for end events).</param>
    /// <param name="durationMs">Duration in milliseconds (only for end events).</param>
    /// <param name="errorType">Optional error type (only for failures).</param>
    /// <param name="errorMessage">Optional error message (only for failures).</param>
    /// <returns>A dictionary of structured properties for audit logging.</returns>
    private static Dictionary<string, object?> CreateAuditProperties(
        Guid invocationId,
        string externalToolName,
        string upstreamName,
        Guid correlationId,
        int? argumentsSizeBytes,
        string? argumentsHash,
        bool success = true,
        long durationMs = 0,
        string? errorType = null,
        string? errorMessage = null)
    {
        var properties = new Dictionary<string, object?>
        {
            ["InvocationId"] = invocationId.ToString(),
            ["ExternalToolName"] = externalToolName,
            ["UpstreamName"] = upstreamName,
            ["CorrelationId"] = correlationId.ToString(),
            ["TraceId"] = Activity.Current?.TraceId.ToString(),
            ["SpanId"] = Activity.Current?.SpanId.ToString()
        };

        if (argumentsSizeBytes.HasValue)
        {
            properties["ArgumentsSizeBytes"] = argumentsSizeBytes.Value;
        }

        if (!string.IsNullOrWhiteSpace(argumentsHash))
        {
            properties["ArgumentsHash"] = argumentsHash;
        }

        if (durationMs > 0)
        {
            properties["DurationMs"] = durationMs;
        }

        properties["Success"] = success;

        if (!success)
        {
            properties["ErrorType"] = errorType ?? "Unknown";
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                properties["ErrorMessage"] = errorMessage;
            }
        }

        return properties;
    }

    /// <summary>
    /// Emits a structured log event to the Contextify logging system.
    /// Only emits if IContextifyLogging is registered in the DI container.
    /// </summary>
    /// <param name="level">The log level for the event.</param>
    /// <param name="message">The log message describing the event.</param>
    /// <param name="properties">Structured properties for the event.</param>
    /// <param name="eventId">Optional event ID for categorization.</param>
    private void EmitContextifyLogEvent(
        ContextifyLogLevel level,
        string message,
        Dictionary<string, object?> properties,
        int? eventId = null)
    {
        if (_contextifyLogging is null)
        {
            return;
        }

        try
        {
            var logEvent = ContextifyLogEvent.Create(
                level,
                message,
                category: "GatewayAudit",
                properties: properties,
                eventId: eventId);

            _contextifyLogging.Log(logEvent);
        }
        catch
        {
            // Swallow exceptions from audit logging to prevent cascading failures
            // Audit logging should never impact the primary request processing path
        }
    }

    /// <summary>
    /// Calculates a hash of the arguments content for change detection.
    /// Provides a way to track argument changes without logging sensitive data.
    /// Uses a simple hash algorithm optimized for performance in high-concurrency scenarios.
    /// </summary>
    /// <param name="arguments">The arguments JSON object to hash.</param>
    /// <returns>A hexadecimal string representing the hash of the arguments.</returns>
    public static string? CalculateArgumentsHash(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        // Use a simple hash for performance - not for cryptographic purposes
        // This allows tracking changes in arguments without logging the content
        var hash = new XXHash32();
        var bytes = System.Text.Encoding.UTF8.GetBytes(arguments);
        var hashBytes = hash.ComputeHash(bytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Calculates the approximate size of arguments in bytes.
    /// Used for volume tracking and analytics without examining argument content.
    /// </summary>
    /// <param name="arguments">The arguments JSON string to measure.</param>
    /// <returns>The size of the arguments in bytes, or null if arguments is null.</returns>
    public static int? CalculateArgumentsSize(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        return System.Text.Encoding.UTF8.GetByteCount(arguments);
    }
}

/// <summary>
/// Provides XXHash32 implementation for fast non-cryptographic hashing.
/// Used for argument content hashing in audit scenarios.
/// Optimized for performance in high-throughput scenarios.
/// </summary>
internal sealed class XXHash32
{
    private const uint Prime1 = 2654435761U;
    private const uint Prime2 = 2246822519U;
    private const uint Prime3 = 3266489917U;
    private const uint Prime4 = 668265263U;
    private const uint Prime5 = 374761393U;

    private uint _state;

    /// <summary>
    /// Computes the XXHash32 hash of the input data.
    /// </summary>
    /// <param name="data">The input bytes to hash.</param>
    /// <returns>The 32-bit hash value as a byte array.</returns>
    public byte[] ComputeHash(byte[] data)
    {
        _state = Prime5;

        int index = 0;
        int length = data.Length;

        // Process 16-byte blocks
        while (length >= 16)
        {
            uint lane = BitConverter.ToUInt32(data, index);
            _state = RotateLeft(_state + lane * Prime2, 13) * Prime1;

            lane = BitConverter.ToUInt32(data, index + 4);
            _state = RotateLeft(_state + lane * Prime2, 13) * Prime1;

            lane = BitConverter.ToUInt32(data, index + 8);
            _state = RotateLeft(_state + lane * Prime2, 13) * Prime1;

            lane = BitConverter.ToUInt32(data, index + 12);
            _state = RotateLeft(_state + lane * Prime2, 13) * Prime1;

            index += 16;
            length -= 16;
        }

        // Process remaining bytes
        uint remaining = 0;
        uint shift = 0;

        while (length > 0)
        {
            remaining |= (uint)data[index] << (int)shift;
            shift += 8;
            index++;
            length--;
        }

        if (shift > 0)
        {
            _state = RotateLeft(_state + remaining * Prime2, 13) * Prime1;
        }

        // Final avalanche
        _state ^= _state >> 15;
        _state *= Prime2;
        _state ^= _state >> 13;
        _state *= Prime3;
        _state ^= _state >> 16;

        return BitConverter.GetBytes(_state);
    }

    /// <summary>
    /// Rotates the bits of a 32-bit unsigned integer left by a specified amount.
    /// </summary>
    /// <param name="value">The value to rotate.</param>
    /// <param name="count">The number of bits to rotate.</param>
    /// <returns>The rotated value.</returns>
    private static uint RotateLeft(uint value, int count)
    {
        return (value << count) | (value >> (32 - count));
    }
}
