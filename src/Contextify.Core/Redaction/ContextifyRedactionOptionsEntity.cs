namespace Contextify.Core.Redaction;

/// <summary>
/// Configuration options for redaction of sensitive information in Contextify.
/// Defines settings for field-name based JSON redaction and optional pattern-based text redaction.
/// Designed for high-performance scenarios with minimal allocation overhead when disabled.
/// </summary>
public sealed class ContextifyRedactionOptionsEntity
{
    /// <summary>
    /// Gets or sets a value indicating whether redaction is enabled.
    /// When false, all redaction operations return the input unchanged for zero performance overhead.
    /// Default value is false (opt-in for safety).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets the list of JSON field names to redact.
    /// Field names are case-insensitive and matched against property names in JSON objects.
    /// When a matching field is found, its value is replaced with "[REDACTED]".
    /// Common values include: "password", "secret", "token", "apiKey", "creditCard", "ssn".
    /// This field is read-only after initialization; use the constructor to set initial values.
    /// </summary>
    /// <remarks>
    /// Field-name matching is performed without regex for optimal performance.
    /// Redaction applies recursively to nested objects and arrays.
    /// Consider the security implications of which fields to redact in your specific domain.
    /// </remarks>
    public IReadOnlyList<string> RedactJsonFields { get; }

    /// <summary>
    /// Gets the list of regex patterns for text redaction.
    /// Patterns are applied only when explicitly enabled to avoid performance impact.
    /// Patterns should be specific to avoid false positives; consider using word boundaries.
    /// This field is read-only after initialization; use the constructor to set initial values.
    /// </summary>
    /// <remarks>
    /// WARNING: Regex redaction has significant performance overhead.
    /// Only enable for text content when field-name based JSON redaction is insufficient.
    /// Patterns are compiled and cached on first use for subsequent operations.
    /// Consider using simpler string replacement when possible.
    /// </remarks>
    public IReadOnlyList<string> RedactPatterns { get; }

    /// <summary>
    /// Initializes a new instance with default values for safe redaction behavior.
    /// Redaction is disabled by default; no fields or patterns are configured.
    /// </summary>
    public ContextifyRedactionOptionsEntity()
        : this(
            enabled: false,
            redactJsonFields: Array.Empty<string>(),
            redactPatterns: Array.Empty<string>())
    {
    }

    /// <summary>
    /// Initializes a new instance with specified redaction configuration.
    /// Creates immutable lists for field names and patterns to prevent runtime modification.
    /// </summary>
    /// <param name="enabled">Whether redaction should be enabled. Default is false for safety.</param>
    /// <param name="redactJsonFields">List of JSON field names to redact. Case-insensitive matching.</param>
    /// <param name="redactPatterns">List of regex patterns for text redaction. Use sparingly for performance.</param>
    /// <exception cref="ArgumentNullException">Thrown when redactJsonFields or redactPatterns is null.</exception>
    public ContextifyRedactionOptionsEntity(
        bool enabled,
        IReadOnlyList<string> redactJsonFields,
        IReadOnlyList<string> redactPatterns)
    {
        ArgumentNullException.ThrowIfNull(redactJsonFields);
        ArgumentNullException.ThrowIfNull(redactPatterns);

        Enabled = enabled;
        RedactJsonFields = redactJsonFields;
        RedactPatterns = redactPatterns;
    }

    /// <summary>
    /// Creates a new options instance with redaction enabled and specified field names.
    /// Convenience method for enabling field-based redaction without pattern matching.
    /// </summary>
    /// <param name="redactJsonFields">The JSON field names to redact.</param>
    /// <returns>A new ContextifyRedactionOptionsEntity with enabled redaction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when redactJsonFields is null.</exception>
    public static ContextifyRedactionOptionsEntity CreateWithFields(
        params string[] redactJsonFields)
    {
        ArgumentNullException.ThrowIfNull(redactJsonFields);
        return new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: redactJsonFields,
            redactPatterns: Array.Empty<string>());
    }

    /// <summary>
    /// Creates a new options instance with redaction enabled, field names, and text patterns.
    /// Convenience method for enabling both field-based and pattern-based redaction.
    /// </summary>
    /// <param name="redactJsonFields">The JSON field names to redact.</param>
    /// <param name="redactPatterns">The regex patterns for text redaction.</param>
    /// <returns>A new ContextifyRedactionOptionsEntity with full redaction enabled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public static ContextifyRedactionOptionsEntity CreateWithFieldsAndPatterns(
        string[] redactJsonFields,
        string[] redactPatterns)
    {
        ArgumentNullException.ThrowIfNull(redactJsonFields);
        ArgumentNullException.ThrowIfNull(redactPatterns);
        return new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: redactJsonFields,
            redactPatterns: redactPatterns);
    }
}
