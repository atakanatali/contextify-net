using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Contextify.Actions.Abstractions.Models;

/// <summary>
/// Data transfer object representing the result of a tool invocation in the Contextify pipeline.
/// Encapsulates both structured JSON output and human-readable text content, supporting various
/// response formats from tools including simple text, complex objects, binary data, and errors.
/// This record is immutable by design for thread-safety and to prevent post-invocation modification.
/// </summary>
public sealed record ContextifyToolResultDto
{
    /// <summary>
    /// Gets the human-readable text content of the tool result.
    /// This is the primary output format for text-based tools and serves as a fallback
    /// when structured output cannot be provided.
    /// Can be null for tools that only return structured data.
    /// </summary>
    public string? TextContent { get; init; }

    /// <summary>
    /// Gets the structured JSON output of the tool result as a JsonNode.
    /// Allows for complex, hierarchical data structures including objects, arrays, and primitives.
    /// When populated, this provides machine-readable output for client applications.
    /// Can be null for tools that only return plain text.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "JsonNode is a complex type, not a simple array. Returning as-is allows for flexible JSON structures.")]
    public JsonNode? JsonContent { get; init; }

    /// <summary>
    /// Gets the MIME content type of the result.
    /// Indicates the format of the data for proper client-side handling.
    /// Common values include "text/plain", "application/json", "image/png", etc.
    /// Null implies "text/plain" as the default content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets optional error information if the tool invocation failed.
    /// When present, indicates that the result represents an error condition rather than successful execution.
    /// Null indicates successful execution (even with empty output).
    /// </summary>
    public ContextifyToolErrorDto? Error { get; init; }

    /// <summary>
    /// Gets a value indicating whether the tool invocation was successful.
    /// True when Error is null; false when an error is present.
    /// </summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>
    /// Gets a value indicating whether the tool invocation failed.
    /// True when Error is not null; false when the invocation succeeded.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => Error is not null;

    /// <summary>
    /// Initializes a new instance representing a successful text-only result.
    /// </summary>
    /// <param name="textContent">The text content of the result. Can be null or empty.</param>
    /// <param name="contentType">The MIME content type. Null defaults to "text/plain".</param>
    /// <exception cref="ArgumentException">Thrown when contentType is an empty string.</exception>
    public ContextifyToolResultDto(string? textContent, string? contentType = null)
    {
        if (contentType == string.Empty)
        {
            throw new ArgumentException("Content type cannot be an empty string. Use null for default.", nameof(contentType));
        }

        TextContent = textContent;
        JsonContent = null;
        ContentType = contentType;
        Error = null;
    }

    /// <summary>
    /// Initializes a new instance representing a successful result with structured JSON content.
    /// </summary>
    /// <param name="jsonContent">The JSON content of the result. Must not be null.</param>
    /// <param name="contentType">The MIME content type. Null defaults to "application/json".</param>
    /// <exception cref="ArgumentNullException">Thrown when jsonContent is null.</exception>
    /// <exception cref="ArgumentException">Thrown when contentType is an empty string.</exception>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "JsonNode is a complex type, not a simple array. Returning as-is allows for flexible JSON structures.")]
    public ContextifyToolResultDto(JsonNode jsonContent, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(jsonContent);

        if (contentType == string.Empty)
        {
            throw new ArgumentException("Content type cannot be an empty string. Use null for default.", nameof(contentType));
        }

        TextContent = null;
        JsonContent = jsonContent;
        ContentType = contentType ?? "application/json";
        Error = null;
    }

    /// <summary>
    /// Initializes a new instance representing a result with both text and JSON content.
    /// </summary>
    /// <param name="textContent">The human-readable text content. Can be null or empty.</param>
    /// <param name="jsonContent">The structured JSON content. Can be null.</param>
    /// <param name="contentType">The MIME content type. Null defaults to "application/json" if jsonContent is present, otherwise "text/plain".</param>
    /// <exception cref="ArgumentException">Thrown when contentType is an empty string.</exception>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "JsonNode is a complex type, not a simple array. Returning as-is allows for flexible JSON structures.")]
    public ContextifyToolResultDto(string? textContent, JsonNode? jsonContent, string? contentType = null)
    {
        if (contentType == string.Empty)
        {
            throw new ArgumentException("Content type cannot be an empty string. Use null for default.", nameof(contentType));
        }

        TextContent = textContent;
        JsonContent = jsonContent;
        ContentType = contentType ?? (jsonContent is not null ? "application/json" : null);
        Error = null;
    }

    /// <summary>
    /// Initializes a new instance representing a failed tool invocation with error information.
    /// </summary>
    /// <param name="error">The error details. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when error is null.</exception>
    public ContextifyToolResultDto(ContextifyToolErrorDto error)
    {
        ArgumentNullException.ThrowIfNull(error);

        TextContent = error.Message;
        JsonContent = null;
        ContentType = "text/plain";
        Error = error;
    }

    /// <summary>
    /// Creates a successful result with only text content.
    /// </summary>
    /// <param name="textContent">The text content. Can be null or empty.</param>
    /// <param name="contentType">The optional MIME content type.</param>
    /// <returns>A new ContextifyToolResultDto representing success.</returns>
    public static ContextifyToolResultDto Success(string? textContent = null, string? contentType = null)
        => new(textContent, contentType);

    /// <summary>
    /// Creates a successful result with structured JSON content.
    /// </summary>
    /// <param name="jsonContent">The JSON content. Must not be null.</param>
    /// <param name="contentType">The optional MIME content type.</param>
    /// <returns>A new ContextifyToolResultDto representing success.</returns>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "JsonNode is a complex type, not a simple array. Returning as-is allows for flexible JSON structures.")]
    public static ContextifyToolResultDto Success(JsonNode jsonContent, string? contentType = null)
        => new(jsonContent, contentType);

    /// <summary>
    /// Creates a failed result with error information.
    /// </summary>
    /// <param name="errorCode">The error code or category.</param>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="details">Optional structured error details as JSON.</param>
    /// <param name="isTransient">Whether the error is transient (retryable).</param>
    /// <returns>A new ContextifyToolResultDto representing failure.</returns>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "JsonNode is a complex type, not a simple array. Returning as-is allows for flexible JSON structures.")]
    public static ContextifyToolResultDto Failure(
        string errorCode,
        string message,
        JsonNode? details = null,
        bool isTransient = false)
        => new(new ContextifyToolErrorDto(errorCode, message, details, isTransient));

    /// <summary>
    /// Creates a failed result from an exception.
    /// </summary>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="isTransient">Whether the error is transient (retryable). Defaults to false.</param>
    /// <returns>A new ContextifyToolResultDto representing failure.</returns>
    public static ContextifyToolResultDto FromException(Exception exception, bool isTransient = false)
        => new(new ContextifyToolErrorDto(
            exception.GetType().Name,
            exception.Message,
            isTransient));

    /// <summary>
    /// Serializes the result to a JSON string representation.
    /// </summary>
    /// <returns>A JSON string representing the result.</returns>
    public string ToJson()
    {
        var result = new
        {
            isSuccess = IsSuccess,
            textContent = TextContent,
            jsonContent = JsonContent,
            contentType = ContentType,
            error = Error is not null ? new { Error.ErrorCode, Error.Message, Error.IsTransient } : null
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Returns a string representation of the result.
    /// If TextContent is available, returns it; otherwise returns a JSON representation.
    /// </summary>
    /// <returns>A string representation of the result.</returns>
    public override string ToString()
    {
        if (!string.IsNullOrEmpty(TextContent))
        {
            return TextContent;
        }

        return JsonContent?.ToString() ?? (Error?.Message ?? "Empty result");
    }
}
