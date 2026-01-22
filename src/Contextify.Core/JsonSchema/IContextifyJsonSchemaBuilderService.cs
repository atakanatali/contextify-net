namespace Contextify.Core.JsonSchema;

/// <summary>
/// Service for building JSON Schema representations from .NET types using reflection.
/// Generates schemas compatible with JSON Schema Draft 2020-12 specification.
/// Supports primitives, enums, arrays, dictionaries, records, and classes with nullable annotations.
/// Results are cached by type for performance in high-throughput scenarios.
/// </summary>
public interface IContextifyJsonSchemaBuilderService
{
    /// <summary>
    /// Builds a JSON Schema string for the specified type using reflection.
    /// The schema is generated deterministically based on type structure and nullable annotations.
    /// Results are cached per Type instance for optimal performance.
    /// </summary>
    /// <param name="type">The .NET type to generate a JSON Schema for.</param>
    /// <returns>A JSON Schema string conforming to JSON Schema Draft 2020-12 specification.</returns>
    /// <exception cref="ArgumentNullException">Thrown when type is null.</exception>
    /// <remarks>
    /// Supported type mappings:
    /// - Primitives: string, bool, int, long, float, double, decimal, DateTime, DateTimeOffset, Guid, Uri
    /// - Nullable primitives: Same as non-nullable with optional flag
    /// - Enums: String-based enum with all values as enum array
    /// - Arrays/IList: Array type with items schema
    /// - Dictionaries/IDictionary: Object type with additionalProperties schema
    /// - Records/Classes: Object type with properties based on public readable properties
    ///
    /// Required fields are determined by nullable reference type annotations:
    /// - Non-nullable value types and reference types marked as required
    /// - Nullable reference types (string?) and nullable value types (int?) marked as optional
    ///
    /// The generated schema is deterministic and cached for subsequent calls.
    /// </remarks>
    string BuildJsonSchema(Type type);

    /// <summary>
    /// Builds a JSON Schema string for the specified generic type parameter.
    /// Type-safe convenience overload for generic type schema generation.
    /// </summary>
    /// <typeparam name="T">The .NET type to generate a JSON Schema for.</typeparam>
    /// <returns>A JSON Schema string conforming to JSON Schema Draft 2020-12 specification.</returns>
    string BuildJsonSchema<T>();

    /// <summary>
    /// Clears the internal schema cache.
    /// Useful for testing or when type structures change at runtime (rare scenarios).
    /// </summary>
    void ClearCache();
}
