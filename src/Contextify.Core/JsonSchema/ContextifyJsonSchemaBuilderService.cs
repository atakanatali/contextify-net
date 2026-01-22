using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Contextify.Core.JsonSchema;

/// <summary>
/// Service for building JSON Schema representations from .NET types using reflection.
/// Generates schemas compatible with JSON Schema Draft 2020-12 specification.
/// Implements deterministic schema generation with concurrent caching for high-throughput scenarios.
/// </summary>
public sealed class ContextifyJsonSchemaBuilderService : IContextifyJsonSchemaBuilderService
{
    // JSON Schema specification version
    private const string JsonSchemaVersion = "https://json-schema.org/draft/2020-12/schema";

    // Cache for generated schemas keyed by Type for thread-safe concurrent access
    private readonly ConcurrentDictionary<Type, string> _schemaCache = new();

    private readonly ILogger<ContextifyJsonSchemaBuilderService>? _logger;

    /// <summary>
    /// Initializes a new instance with optional logging support.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information during schema generation.</param>
    public ContextifyJsonSchemaBuilderService(ILogger<ContextifyJsonSchemaBuilderService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds a JSON Schema string for the specified type using reflection.
    /// Schemas are cached by Type instance for optimal performance in high-throughput scenarios.
    /// </summary>
    /// <param name="type">The .NET type to generate a JSON Schema for.</param>
    /// <returns>A JSON Schema string conforming to JSON Schema Draft 2020-12 specification.</returns>
    /// <exception cref="ArgumentNullException">Thrown when type is null.</exception>
    public string BuildJsonSchema(Type type)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        // Return cached schema if available
        if (_schemaCache.TryGetValue(type, out var cachedSchema))
        {
            _logger?.LogDebug("Using cached JSON Schema for type {TypeName}", type.Name);
            return cachedSchema;
        }

        // Build new schema and cache it
        var schema = BuildJsonSchemaInternal(type);
        _schemaCache.TryAdd(type, schema);
        _logger?.LogDebug("Generated and cached JSON Schema for type {TypeName}", type.Name);

        return schema;
    }

    /// <summary>
    /// Builds a JSON Schema string for the specified generic type parameter.
    /// Type-safe convenience overload that uses type inference.
    /// </summary>
    /// <typeparam name="T">The .NET type to generate a JSON Schema for.</typeparam>
    /// <returns>A JSON Schema string conforming to JSON Schema Draft 2020-12 specification.</returns>
    public string BuildJsonSchema<T>()
    {
        return BuildJsonSchema(typeof(T));
    }

    /// <summary>
    /// Clears the internal schema cache.
    /// Primarily used for testing scenarios or when type structures change at runtime.
    /// </summary>
    public void ClearCache()
    {
        _schemaCache.Clear();
        _logger?.LogDebug("JSON Schema cache cleared");
    }

    /// <summary>
    /// Internal method that performs the actual schema generation using reflection.
    /// Handles type resolution, property discovery, and JSON Schema structure construction.
    /// </summary>
    /// <param name="type">The .NET type to generate a schema for.</param>
    /// <returns>A JSON Schema string representing the type structure.</returns>
    private string BuildJsonSchemaInternal(Type type)
    {
        // Unwrap nullable wrappers to get the underlying type
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        // Use MemoryStream to avoid allocations when building JSON
        using var memoryStream = new MemoryStream();
        using var writer = new Utf8JsonWriter(memoryStream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("$schema", JsonSchemaVersion);
        writer.WriteString("type", GetJsonSchemaType(underlyingType));

        // Handle different type categories
        if (underlyingType.IsEnum)
        {
            WriteEnumSchema(writer, underlyingType);
        }
        else if (IsDictionaryType(underlyingType))
        {
            WriteDictionarySchema(writer, underlyingType);
        }
        else if (IsArrayType(underlyingType))
        {
            WriteArraySchema(writer, underlyingType);
        }
        else if (IsObjectOrRecordType(underlyingType))
        {
            WriteObjectSchema(writer, underlyingType);
        }

        writer.WriteEndObject();
        writer.Flush();

        return JsonDocument.Parse(memoryStream.ToArray()).RootElement.GetRawText();
    }

    /// <summary>
    /// Maps .NET types to JSON Schema type strings.
    /// Handles primitive types and value types according to JSON Schema specification.
    /// </summary>
    /// <param name="type">The .NET type to map.</param>
    /// <returns>The corresponding JSON Schema type identifier.</returns>
    private static string GetJsonSchemaType(Type type)
    {
        // String types
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid))
        {
            return "string";
        }

        // Boolean type
        if (type == typeof(bool))
        {
            return "boolean";
        }

        // Numeric types - integer
        if (type == typeof(byte) || type == typeof(sbyte) ||
            type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong))
        {
            return "integer";
        }

        // Numeric types - number
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return "number";
        }

        // Date/time types represented as strings
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return "string";
        }

        // URI type represented as string
        if (type == typeof(Uri))
        {
            return "string";
        }

        // Enum type represented as string with enum constraint
        if (type.IsEnum)
        {
            return "string";
        }

        // Dictionary types represented as object (check BEFORE array since dictionaries are also Enumerable)
        if (IsDictionaryType(type))
        {
            return "object";
        }
 
        // Array types
        if (IsArrayType(type))
        {
            return "array";
        }

        // Object types (classes, records)
        if (IsObjectOrRecordType(type))
        {
            return "object";
        }

        // Default to string for unknown types
        return "string";
    }

    /// <summary>
    /// Determines if the type represents a collection/array.
    /// Checks for Array, IEnumerable, IList, and ICollection interfaces.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is an array or collection type.</returns>
    private static bool IsArrayType(Type type)
    {
        if (type == typeof(string))
        {
            return false;
        }
 
        if (type.IsArray)
        {
            return true;
        }
 
        // Check for generic collection interfaces
        var isGenericCollection = type.GetInterfaces().Any(i => 
            i.IsGenericType && (
            i.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
            i.GetGenericTypeDefinition() == typeof(IList<>) ||
            i.GetGenericTypeDefinition() == typeof(ICollection<>) ||
            i.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ||
            i.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)
        ));
 
        if (isGenericCollection)
        {
            return true;
        }
 
        // Also check if the type itself is one of these interfaces
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            return def == typeof(IEnumerable<>) ||
                   def == typeof(IList<>) ||
                   def == typeof(ICollection<>) ||
                   def == typeof(IReadOnlyList<>) ||
                   def == typeof(IReadOnlyCollection<>);
        }
 
        return false;
    }

    /// <summary>
    /// Determines if the type represents a dictionary/map.
    /// Checks for IDictionary and IReadOnlyDictionary interfaces.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is a dictionary type.</returns>
    private static bool IsDictionaryType(Type type)
    {
        return type.GetInterface("IDictionary`2") != null ||
               type.GetInterface("IReadOnlyDictionary`2") != null;
    }

    /// <summary>
    /// Determines if the type is a complex object type (class or record).
    /// Excludes primitives, enums, arrays, dictionaries, and strings.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is a class or record type.</returns>
    private static bool IsObjectOrRecordType(Type type)
    {
        return type.IsClass && type != typeof(string) &&
               !IsArrayType(type) &&
               !IsDictionaryType(type) &&
               !typeof(Delegate).IsAssignableFrom(type);
    }

    /// <summary>
    /// Writes JSON Schema for enum types.
    /// Includes all enum values in the enum array constraint.
    /// </summary>
    /// <param name="writer">The JSON writer to write to.</param>
    /// <param name="enumType">The enum type to generate schema for.</param>
    private static void WriteEnumSchema(Utf8JsonWriter writer, Type enumType)
    {
        var enumValues = Enum.GetValues(enumType)
            .Cast<Enum>()
            .Select(e => e.ToString())
            .Order()
            .ToList();

        writer.WritePropertyName("enum");
        writer.WriteStartArray();
        foreach (var value in enumValues)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes JSON Schema for array types.
    /// Includes the items schema describing the array element type.
    /// </summary>
    /// <param name="writer">The JSON writer to write to.</param>
    /// <param name="arrayType">The array or collection type to generate schema for.</param>
    private void WriteArraySchema(Utf8JsonWriter writer, Type arrayType)
    {
        // Get the element type from the array
        Type? elementType = null;

        if (arrayType.IsArray)
        {
            elementType = arrayType.GetElementType();
        }
        else
        {
            // Try to get the generic argument from IEnumerable<T>, IList<T>, etc.
            var enumerableInterface = arrayType.GetInterface("IEnumerable`1");
            if (enumerableInterface != null)
            {
                elementType = enumerableInterface.GetGenericArguments()[0];
            }
        }

        if (elementType != null)
        {
            var elementSchema = BuildJsonSchemaInternal(elementType);
            var elementJson = JsonDocument.Parse(elementSchema).RootElement;

            writer.WritePropertyName("items");
            JsonElementToWriter(writer, elementJson);
        }
    }

    /// <summary>
    /// Writes JSON Schema for dictionary types.
    /// Uses additionalProperties to describe the value type schema.
    /// </summary>
    /// <param name="writer">The JSON writer to write to.</param>
    /// <param name="dictionaryType">The dictionary type to generate schema for.</param>
    private void WriteDictionarySchema(Utf8JsonWriter writer, Type dictionaryType)
    {
        // Get the value type from the dictionary
        Type? valueType = null;

        var dictionaryInterface = dictionaryType.GetInterface("IDictionary`2") ??
                                  dictionaryType.GetInterface("IReadOnlyDictionary`2");

        if (dictionaryInterface != null)
        {
            var genericArgs = dictionaryInterface.GetGenericArguments();
            valueType = genericArgs.Length >= 2 ? genericArgs[1] : null;
        }

        if (valueType != null)
        {
            var valueSchema = BuildJsonSchemaInternal(valueType);
            var valueJson = JsonDocument.Parse(valueSchema).RootElement;

            writer.WritePropertyName("additionalProperties");
            JsonElementToWriter(writer, valueJson);
        }
    }

    /// <summary>
    /// Writes JSON Schema for object types (classes and records).
    /// Discovers properties using reflection and determines required status from nullable annotations.
    /// </summary>
    /// <param name="writer">The JSON writer to write to.</param>
    /// <param name="objectType">The object type to generate schema for.</param>
    private void WriteObjectSchema(Utf8JsonWriter writer, Type objectType)
    {
        // Get public instance properties with getters
        var properties = objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name)
            .ToList();

        if (properties.Count > 0)
        {
            writer.WritePropertyName("properties");
            writer.WriteStartObject();

            foreach (var property in properties)
            {
                writer.WritePropertyName(property.Name);

                // Build schema for the property type
                var propertyType = property.PropertyType;
                var propertySchema = BuildJsonSchemaInternal(propertyType);
                var propertyJson = JsonDocument.Parse(propertySchema).RootElement;

                // Check if property is required (non-nullable)
                var isRequired = !IsPropertyNullable(property);

                // Clone the schema and add required information
                var propertySchemaDoc = JsonDocument.Parse(propertySchema);
                var propertySchemaClone = JsonDocument.Parse(propertySchema).RootElement.Clone();

                // Write the property schema with potential required info
                WritePropertySchema(writer, propertySchemaClone, isRequired, propertyType);
            }

            writer.WriteEndObject();

            // Write required array with names of required properties
            var requiredProperties = properties
                .Where(p => !IsPropertyNullable(p))
                .Select(p => p.Name)
                .OrderBy(name => name)
                .ToList();

            if (requiredProperties.Count > 0)
            {
                writer.WritePropertyName("required");
                writer.WriteStartArray();
                foreach (var required in requiredProperties)
                {
                    writer.WriteStringValue(required);
                }
                writer.WriteEndArray();
            }
        }
    }

    /// <summary>
    /// Writes a property schema with the appropriate structure.
    /// Handles nullable reference types and adds nullable annotation to the schema.
    /// </summary>
    /// <param name="writer">The JSON writer to write to.</param>
    /// <param name="schemaElement">The schema element for the property type.</param>
    /// <param name="isRequired">Whether the property is required (non-nullable).</param>
    /// <param name="propertyType">The type of the property.</param>
    private static void WritePropertySchema(Utf8JsonWriter writer, JsonElement schemaElement, bool isRequired, Type propertyType)
    {
        writer.WriteStartObject();

        // Copy all properties from the base schema
        foreach (var property in schemaElement.EnumerateObject())
        {
            if (property.Name != "$schema") // Skip schema version in nested schemas
            {
                property.WriteTo(writer);
            }
        }

        // Add nullable annotation for optional properties
        if (!isRequired && schemaElement.TryGetProperty("type", out var typeProperty))
        {
            // For nullable properties, we can add nullable: true or use oneOf
            // Using a simple nullable extension property
            writer.WriteBoolean("nullable", true);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Determines if a property is nullable based on its type and nullable annotation context.
    /// Checks both nullable value types (int?) and nullable reference types (string?).
    /// </summary>
    /// <param name="property">The property info to check.</param>
    /// <returns>True if the property is nullable (optional), false if required.</returns>
    private static bool IsPropertyNullable(PropertyInfo property)
    {
        // Check for nullable value types (int?, DateTime?, etc.)
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType);
        if (underlyingType != null)
        {
            return true; // It's a nullable value type
        }

        // For reference types, check the nullable attribute
        // This checks for string? and other nullable reference types
        var nullableContext = property.GetCustomAttributes(typeof(NullableAttribute), false)
            .OfType<NullableAttribute>()
            .FirstOrDefault();

        if (nullableContext != null)
        {
            // The NullableAttribute contains a byte array flag
            // 0 = not nullable, 1 = nullable, 2 = only applies to nested types
            var flag = nullableContext.Flag;
            if (flag.Length > 0 && flag[0] == 1)
            {
                return true;
            }
        }

        // Check for nullable reference type via the compiler-generated attribute
        var nullableBoolContext = property.GetCustomAttributes(typeof(NullableContextAttribute), false)
            .OfType<NullableContextAttribute>()
            .FirstOrDefault();

        if (nullableBoolContext != null)
        {
            // Flag 1 = nullable context, Flag 2 = non-nullable context
            return nullableBoolContext.Flag == 1;
        }

        // Default: reference types are nullable, value types are not
        return !property.PropertyType.IsValueType;
    }

    /// <summary>
    /// Writes a JsonElement to the Utf8JsonWriter.
    /// Recursively handles objects, arrays, and primitive values.
    /// </summary>
    /// <param name="writer">The JSON writer to write to.</param>
    /// <param name="element">The JSON element to write.</param>
    private static void JsonElementToWriter(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    JsonElementToWriter(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    JsonElementToWriter(writer, item);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longValue))
                {
                    writer.WriteNumberValue(longValue);
                }
                else
                {
                    writer.WriteNumberValue(element.GetDouble());
                }
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
        }
    }
}

/// <summary>
/// Internal attribute for nullable reference type annotation.
/// Generated by C# compiler for nullable reference type contexts.
/// </summary>
[ExcludeFromCodeCoverage]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
internal sealed class NullableAttribute : Attribute
{
    /// <summary>
    /// Gets the nullable flag byte array indicating nullability.
    /// </summary>
    public byte[] Flag { get; }

    /// <summary>
    /// Initializes a new instance with the specified nullable flag.
    /// </summary>
    /// <param name="flag">The nullable flag value.</param>
    public NullableAttribute(byte flag)
    {
        Flag = [flag];
    }

    /// <summary>
    /// Initializes a new instance with the specified nullable flags.
    /// </summary>
    /// <param name="flags">Array of nullable flags for nested scenarios.</param>
    public NullableAttribute(byte[] flags)
    {
        Flag = flags;
    }
}

/// <summary>
/// Internal attribute for nullable context annotation.
/// Generated by C# compiler for nullable reference type contexts.
/// </summary>
[ExcludeFromCodeCoverage]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
internal sealed class NullableContextAttribute : Attribute
{
    /// <summary>
    /// Gets the nullable context flag.
    /// 1 = nullable context, 2 = non-nullable context.
    /// </summary>
    public byte Flag { get; }

    /// <summary>
    /// Initializes a new instance with the specified context flag.
    /// </summary>
    /// <param name="flag">The context flag value.</param>
    public NullableContextAttribute(byte flag)
    {
        Flag = flag;
    }
}
