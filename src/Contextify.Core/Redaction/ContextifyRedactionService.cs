using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Contextify.Core.Redaction;

/// <summary>
/// Default implementation of redaction service for sensitive information in text and JSON.
/// Provides fast field-name based JSON redaction without regex for optimal performance.
/// Supports optional pattern-based text redaction when explicitly enabled.
/// Designed for high-concurrency scenarios with millions of requests per second.
/// </summary>
public sealed class ContextifyRedactionService : IContextifyRedactionService
{
    /// <summary>
    /// The replacement text used for redacted values.
    /// Chosen to be clear and unambiguous in logs and responses.
    /// </summary>
    private const string RedactedValue = "[REDACTED]";

    /// <summary>
    /// Cached lowercase hash set of JSON field names for fast case-insensitive matching.
    /// Created once on first access to avoid repeated allocations.
    /// </summary>
    private readonly HashSet<string>? _jsonFieldCache;

    /// <summary>
    /// Cached compiled regex patterns for text redaction.
    /// Created once on first access when patterns are configured.
    /// </summary>
    private readonly Lazy<List<Regex>>? _patternCache;

    /// <summary>
    /// The redaction options defining enabled state, fields, and patterns.
    /// </summary>
    private readonly ContextifyRedactionOptionsEntity _options;

    /// <summary>
    /// Initializes a new instance with the specified redaction options.
    /// Builds field name cache for O(1) lookup performance.
    /// Lazily compiles regex patterns only when text redaction is needed.
    /// </summary>
    /// <param name="options">The redaction configuration options.</param>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public ContextifyRedactionService(ContextifyRedactionOptionsEntity options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // Pre-build lowercase hash set for O(1) field name lookup
        if (options.RedactJsonFields.Count > 0)
        {
            _jsonFieldCache = new HashSet<string>(
                options.RedactJsonFields.Select(f => f.ToLowerInvariant()),
                StringComparer.Ordinal);
        }

        // Lazily compile regex patterns only when needed for text redaction
        if (options.RedactPatterns.Count > 0)
        {
            _patternCache = new Lazy<List<Regex>>(() =>
            {
                return options.RedactPatterns
                    .Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant))
                    .ToList();
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }

    /// <summary>
    /// Redacts sensitive information from a text string using configured patterns.
    /// Returns the input unchanged when redaction is disabled or no patterns are configured.
    /// </summary>
    /// <param name="input">The input text to redact. Can be null or empty.</param>
    /// <returns>
    /// The redacted text string, or the original text if redaction is disabled.
    /// Returns null if the input is null.
    /// </returns>
    /// <remarks>
    /// This method allocates only when patterns are configured and redaction is enabled.
    /// Patterns are applied in order; later patterns can redact portions of earlier results.
    /// For high-performance scenarios, prefer JSON redaction via field names.
    /// </remarks>
    public string? RedactText(string? input)
    {
        // Fast path: disabled or no input
        if (!_options.Enabled || input is null)
        {
            return input;
        }

        // Fast path: no patterns configured
        if (_options.RedactPatterns.Count == 0)
        {
            return input;
        }

        // Apply all regex patterns in sequence
        var result = input;
        var patterns = _patternCache!.Value;

        foreach (var pattern in patterns)
        {
            result = pattern.Replace(result, RedactedValue);
        }

        return result;
    }

    /// <summary>
    /// Redacts sensitive information from a JSON element using field-name matching.
    /// Recursively processes objects and arrays, replacing values of matching fields.
    /// Does not modify the input element; creates a new JSON element with redacted content.
    /// </summary>
    /// <param name="input">The JSON element to redact. Can be null.</param>
    /// <returns>
    /// A new JSON element with sensitive field values redacted, or the original element if redaction is disabled.
    /// Returns null if the input is null.
    /// </summary>
    /// <remarks>
    /// Field name matching is case-insensitive for flexibility with JSON naming conventions.
    /// This method allocates a new JSON document for redaction to avoid modifying the input.
    /// For optimal performance, consider using a streaming JSON processor for large documents.
    /// </remarks>
    [SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly",
        Justification = "Exception messages reference the parameter name for clarity.")]
    public JsonElement? RedactJson(JsonElement? input)
    {
        // Fast path: disabled or no input
        if (!_options.Enabled || input is null)
        {
            return input;
        }

        // Fast path: no fields configured
        if (_options.RedactJsonFields.Count == 0)
        {
            return input;
        }

        return RedactJsonElementRecursive(input.Value);
    }

    /// <summary>
    /// Recursively processes a JSON element to redact sensitive field values.
    /// Handles objects, arrays, and primitives with appropriate redaction logic.
    /// </summary>
    /// <param name="element">The JSON element to process.</param>
    /// <returns>A new JSON element with redacted content.</returns>
    /// <exception cref="JsonException">Thrown when JSON serialization fails unexpectedly.</exception>
    private JsonElement RedactJsonElementRecursive(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return RedactJsonObject(element);

            case JsonValueKind.Array:
                return RedactJsonArray(element);

            default:
                // Primitives (String, Number, True, False, Null) are returned unchanged
                return element;
        }
    }

    /// <summary>
    /// Redacts values in a JSON object based on configured field names.
    /// Creates a new object with redacted values for matching property names.
    /// </summary>
    /// <param name="obj">The JSON object to redact.</param>
    /// <returns>A new JSON object with sensitive values redacted.</returns>
    /// <exception cref="JsonException">Thrown when JSON processing fails unexpectedly.</exception>
    private JsonElement RedactJsonObject(JsonElement obj)
    {
        // Use JsonSerializer to avoid manual JSON construction
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in obj.EnumerateObject())
        {
            if (ShouldRedactField(property.Name))
            {
                // Replace the value with the redaction marker
                dict[property.Name] = JsonSerializer.Deserialize<JsonElement>(
                    $"\"{RedactedValue}\"");
            }
            else
            {
                // Recursively process nested structures
                dict[property.Name] = RedactJsonElementRecursive(property.Value);
            }
        }

        return JsonSerializer.SerializeToElement(dict);
    }

    /// <summary>
    /// Redacts values in a JSON array by processing each element recursively.
    /// Creates a new array with redacted content in nested objects.
    /// </summary>
    /// <param name="array">The JSON array to redact.</param>
    /// <returns>A new JSON array with redacted content.</returns>
    /// <exception cref="JsonException">Thrown when JSON processing fails unexpectedly.</exception>
    private JsonElement RedactJsonArray(JsonElement array)
    {
        var count = array.GetArrayLength();
        var elements = new List<JsonElement>((int)count);

        foreach (var item in array.EnumerateArray())
        {
            elements.Add(RedactJsonElementRecursive(item));
        }

        return JsonSerializer.SerializeToElement(elements);
    }

    /// <summary>
    /// Determines whether a field name should be redacted based on configuration.
    /// Performs case-insensitive matching for flexibility with naming conventions.
    /// </summary>
    /// <param name="fieldName">The field name to check.</param>
    /// <returns>True if the field should be redacted; false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldRedactField(string fieldName)
    {
        // Inline lowercase comparison for cache locality
        return _jsonFieldCache?.Contains(fieldName.ToLowerInvariant()) ?? false;
    }
}
