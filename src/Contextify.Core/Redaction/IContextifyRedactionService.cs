using System.Text.Json;

namespace Contextify.Core.Redaction;

/// <summary>
/// Defines a service for redacting sensitive information from text and JSON content.
/// Provides field-name based JSON redaction and optional pattern-based text redaction.
/// Designed for high-performance scenarios with millions of requests per second.
/// </summary>
public interface IContextifyRedactionService
{
    /// <summary>
    /// Redacts sensitive information from a text string.
    /// Uses pattern-based redaction when explicitly enabled; otherwise returns the input unchanged.
    /// Pattern matching is performed only when RedactPatterns is configured to avoid performance overhead.
    /// </summary>
    /// <param name="input">The input text to redact. Can be null or empty.</param>
    /// <returns>
    /// The redacted text string, or the original text if redaction is disabled.
    /// Returns null if the input is null.
    /// </returns>
    /// <remarks>
    /// For optimal performance, pattern redaction is disabled by default.
    /// Field-name based redaction should be preferred via RedactJson for structured data.
    /// When patterns are configured, they are applied in the order defined in RedactPatterns.
    /// </remarks>
    string? RedactText(string? input);

    /// <summary>
    /// Redacts sensitive information from a JSON element.
    /// Uses fast field-name based redaction without regex for optimal performance.
    /// Recursively processes all nested objects and arrays, masking values of fields matching RedactJsonFields.
    /// Field name matching is case-insensitive to handle varying JSON conventions.
    /// </summary>
    /// <param name="input">The JSON element to redact. Can be null.</param>
    /// <returns>
    /// A new JSON element with sensitive field values redacted, or the original element if redaction is disabled.
    /// Returns null if the input is null.
    /// </returns>
    /// <remarks>
    /// Redaction applies to all JSON value kinds (Object, Array, String, Number, True, False, Null).
    /// For objects: checks each property name against RedactJsonFields and masks values if matched.
    /// For arrays: recursively processes each element.
    /// For primitives: returns unchanged.
    /// Masked values are replaced with the string "[REDACTED]".
    /// This method does not modify the input element; it creates a new JsonElement with redacted content.
    /// </remarks>
    JsonElement? RedactJson(JsonElement? input);
}
