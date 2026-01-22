using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Contextify.Actions.Abstractions.Models;

/// <summary>
/// Data transfer object representing error information from a failed tool invocation.
/// Encapsulates error codes, messages, optional structured details, and retryability information.
/// This record is immutable by design for thread-safety and to prevent modification.
/// </summary>
public sealed record ContextifyToolErrorDto
{
    /// <summary>
    /// Gets the error code or category identifier.
    /// Used for programmatic error handling and classification.
    /// Examples: "VALIDATION_ERROR", "TIMEOUT", "RATE_LIMITED", "INTERNAL_ERROR".
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Gets the human-readable error message.
    /// Provides a description of what went wrong, suitable for display to end users or logging.
    /// Should be non-null and non-empty for proper error reporting.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the optional structured error details.
    /// Provides additional context about the error in a machine-readable format.
    /// Can contain stack traces, validation error details, argument paths, or other diagnostic information.
    /// Null when no additional details are available.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "JsonNode is a complex type, not a simple array. Returning as-is allows for flexible JSON structures.")]
    public JsonNode? Details { get; }

    /// <summary>
    /// Gets a value indicating whether the error is transient (temporary) and retryable.
    /// True indicates that retrying the operation may succeed (e.g., network timeout, temporary service unavailability).
    /// False indicates that the error is permanent and retrying will not help (e.g., validation error, invalid arguments).
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>
    /// Initializes a new instance with error code, message, and retryability information.
    /// </summary>
    /// <param name="errorCode">The error code or category. Must not be null or whitespace.</param>
    /// <param name="message">The error message. Must not be null or whitespace.</param>
    /// <param name="isTransient">Whether the error is transient and retryable.</param>
    /// <exception cref="ArgumentException">Thrown when errorCode or message is null or whitespace.</exception>
    public ContextifyToolErrorDto(string errorCode, string message, bool isTransient)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("Error code cannot be null or whitespace.", nameof(errorCode));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Error message cannot be null or whitespace.", nameof(message));
        }

        ErrorCode = errorCode;
        Message = message;
        Details = null;
        IsTransient = isTransient;
    }

    /// <summary>
    /// Initializes a new instance with error code, message, structured details, and retryability information.
    /// </summary>
    /// <param name="errorCode">The error code or category. Must not be null or whitespace.</param>
    /// <param name="message">The error message. Must not be null or whitespace.</param>
    /// <param name="details">The structured error details. Can be null.</param>
    /// <param name="isTransient">Whether the error is transient and retryable.</param>
    /// <exception cref="ArgumentException">Thrown when errorCode or message is null or whitespace.</exception>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "JsonNode is a complex type, not a simple array. Returning as-is allows for flexible JSON structures.")]
    public ContextifyToolErrorDto(string errorCode, string message, JsonNode? details, bool isTransient)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("Error code cannot be null or whitespace.", nameof(errorCode));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Error message cannot be null or whitespace.", nameof(message));
        }

        ErrorCode = errorCode;
        Message = message;
        Details = details;
        IsTransient = isTransient;
    }

    /// <summary>
    /// Creates a validation error with default settings (non-transient).
    /// </summary>
    /// <param name="message">The validation error message.</param>
    /// <param name="details">Optional validation error details (e.g., field paths, constraints).</param>
    /// <returns>A new ContextifyToolErrorDto representing a validation error.</returns>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "JsonNode is a complex type, not a simple array. Returning as-is allows for flexible JSON structures.")]
    public static ContextifyToolErrorDto ValidationError(string message, JsonNode? details = null)
        => new("VALIDATION_ERROR", message, details, isTransient: false);

    /// <summary>
    /// Creates a timeout error (transient by default).
    /// </summary>
    /// <param name="message">The timeout error message.</param>
    /// <param name="timeoutMilliseconds">Optional timeout duration in milliseconds.</param>
    /// <returns>A new ContextifyToolErrorDto representing a timeout error.</returns>
    public static ContextifyToolErrorDto TimeoutError(string message, int? timeoutMilliseconds = null)
    {
        var details = timeoutMilliseconds.HasValue
            ? new JsonObject { ["timeoutMilliseconds"] = timeoutMilliseconds.Value }
            : null;

        return new ContextifyToolErrorDto("TIMEOUT", message, details, isTransient: true);
    }

    /// <summary>
    /// Creates a rate limit error (transient by default).
    /// </summary>
    /// <param name="message">The rate limit error message.</param>
    /// <param name="retryAfterSeconds">Optional suggested retry delay in seconds.</param>
    /// <returns>A new ContextifyToolErrorDto representing a rate limit error.</returns>
    public static ContextifyToolErrorDto RateLimitError(string message, int? retryAfterSeconds = null)
    {
        var details = retryAfterSeconds.HasValue
            ? new JsonObject { ["retryAfterSeconds"] = retryAfterSeconds.Value }
            : null;

        return new ContextifyToolErrorDto("RATE_LIMITED", message, details, isTransient: true);
    }

    /// <summary>
    /// Creates a not found error (non-transient).
    /// </summary>
    /// <param name="message">The not found error message.</param>
    /// <param name="resourceType">Optional type of the resource that was not found.</param>
    /// <param name="resourceId">Optional identifier of the resource that was not found.</param>
    /// <returns>A new ContextifyToolErrorDto representing a not found error.</returns>
    public static ContextifyToolErrorDto NotFoundError(
        string message,
        string? resourceType = null,
        string? resourceId = null)
    {
        JsonObject? details = null;

        if (resourceType is not null || resourceId is not null)
        {
            details = new JsonObject();
            if (resourceType is not null)
            {
                details["resourceType"] = resourceType;
            }
            if (resourceId is not null)
            {
                details["resourceId"] = resourceId;
            }
        }

        return new ContextifyToolErrorDto("NOT_FOUND", message, details, isTransient: false);
    }

    /// <summary>
    /// Creates a permission denied error (non-transient).
    /// </summary>
    /// <param name="message">The permission error message.</param>
    /// <param name="requiredPermission">Optional description of the required permission.</param>
    /// <returns>A new ContextifyToolErrorDto representing a permission error.</returns>
    public static ContextifyToolErrorDto PermissionDeniedError(
        string message,
        string? requiredPermission = null)
    {
        var details = requiredPermission is not null
            ? new JsonObject { ["requiredPermission"] = requiredPermission }
            : null;

        return new ContextifyToolErrorDto("PERMISSION_DENIED", message, details, isTransient: false);
    }

    /// <summary>
    /// Creates an internal error (unexpected exception, non-transient by default).
    /// </summary>
    /// <param name="message">The internal error message.</param>
    /// <param name="exceptionType">Optional exception type name.</param>
    /// <returns>A new ContextifyToolErrorDto representing an internal error.</returns>
    public static ContextifyToolErrorDto InternalError(string message, string? exceptionType = null)
    {
        var details = exceptionType is not null
            ? new JsonObject { ["exceptionType"] = exceptionType }
            : null;

        return new ContextifyToolErrorDto("INTERNAL_ERROR", message, details, isTransient: false);
    }

    /// <summary>
    /// Returns a string representation of the error.
    /// </summary>
    /// <returns>A string in the format "[ErrorCode]: Message".</returns>
    public override string ToString()
    {
        return $"[{ErrorCode}]: {Message}";
    }
}
