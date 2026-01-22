using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using JsonSchema = Microsoft.OpenApi.Models.OpenApiSchema;

namespace Contextify.OpenApi.Enrichment;

/// <summary>
/// Service for extracting JSON schemas from OpenAPI operations.
/// Converts OpenAPI schemas into JSON Element format for use in tool descriptors.
/// Handles parameters, request bodies, and response schemas with proper type mapping.
/// </summary>
public sealed class OpenApiSchemaExtractor : IOpenApiSchemaExtractor
{
    private readonly ILogger<OpenApiSchemaExtractor> _logger;

    /// <summary>
    /// Initializes a new instance with logging support.
    /// </summary>
    /// <param name="logger">Logger for diagnostic and troubleshooting information.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public OpenApiSchemaExtractor(ILogger<OpenApiSchemaExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Extracts the combined input schema from an OpenAPI operation.
    /// Merges parameters (path, query, header) and request body into a single JSON schema.
    /// Creates an object schema with properties for each parameter and the request body.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to extract from.</param>
    /// <returns>JSON Element representing the combined input schema, or null if no input is defined.</returns>
    public JsonElement? ExtractInputSchema(OpenApiOperation operation)
    {
        if (operation is null)
        {
            return null;
        }

        var properties = new Dictionary<string, JsonElement>();
        var requiredProperties = new List<string>();

        // Process parameters (path, query, header, cookie)
        foreach (var parameter in operation.Parameters)
        {
            var propertySchema = ConvertSchemaToJson(parameter.Schema);
            if (propertySchema is not null)
            {
                properties[parameter.Name] = propertySchema.Value;

                if (parameter.Required)
                {
                    requiredProperties.Add(parameter.Name);
                }
            }
        }

        // Process request body
        if (operation.RequestBody is not null)
        {
            var bodyContent = GetFirstJsonContent(operation.RequestBody.Content);
            if (bodyContent is not null && bodyContent.Schema is not null)
            {
                var bodySchema = ConvertSchemaToJson(bodyContent.Schema);
                if (bodySchema is not null && bodySchema.Value.ValueKind == JsonValueKind.Object)
                {
                    // Merge body schema properties into the main schema
                    if (bodySchema.Value.TryGetProperty("properties", out var bodyProperties) && 
                        bodyProperties.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in bodyProperties.EnumerateObject())
                        {
                            properties[property.Name] = property.Value;
                        }
                    }

                    // Merge required properties
                    if (bodySchema.Value.TryGetProperty("required", out var requiredArray) &&
                        requiredArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var required in requiredArray.EnumerateArray())
                        {
                            var name = required.GetString();
                            if (!string.IsNullOrWhiteSpace(name) && !requiredProperties.Contains(name))
                            {
                                requiredProperties.Add(name);
                            }
                        }
                    }
                }
            }
        }

        if (properties.Count == 0)
        {
            return null;
        }

        // Build the final JSON schema object
        var schemaBuilder = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object")
        };

        if (properties.Count > 0)
        {
            schemaBuilder["properties"] = JsonSerializer.SerializeToElement(properties);
        }

        if (requiredProperties.Count > 0)
        {
            schemaBuilder["required"] = JsonSerializer.SerializeToElement(requiredProperties);
        }

        return JsonSerializer.SerializeToElement(schemaBuilder);
    }

    /// <summary>
    /// Extracts the response schema from an OpenAPI operation.
    /// Returns the schema for the primary success response (2xx status codes).
    /// Prioritizes 200, then 201, then any other 2xx response.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to extract from.</param>
    /// <param name="warnings">Collection to receive warnings about schema extraction issues.</param>
    /// <returns>JSON Element representing the response schema, or null if no response schema is defined.</returns>
    public JsonElement? ExtractResponseSchema(OpenApiOperation operation, List<string> warnings)
    {
        if (operation is null || operation.Responses.Count == 0)
        {
            return null;
        }

        // Priority order for response codes
        var priorityCodes = new[] { "200", "201", "204", "default", "2XX" };

        foreach (var code in priorityCodes)
        {
            if (operation.Responses.TryGetValue(code, out var response))
            {
                var schema = ExtractSchemaFromResponse(response, warnings);
                if (schema is not null)
                {
                    return schema;
                }
            }
        }

        // Fallback to any 2xx response
        foreach (var responsePair in operation.Responses)
        {
            var statusCode = responsePair.Key;
            if (statusCode.StartsWith("2") || statusCode.Equals("2XX", StringComparison.OrdinalIgnoreCase))
            {
                var schema = ExtractSchemaFromResponse(responsePair.Value, warnings);
                if (schema is not null)
                {
                    return schema;
                }
            }
        }

        // Last resort: any response with content
        foreach (var responsePair in operation.Responses)
        {
            var schema = ExtractSchemaFromResponse(responsePair.Value, warnings);
            if (schema is not null)
            {
                warnings.Add($"Using schema from non-2xx response code: {responsePair.Key}");
                return schema;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the description from an OpenAPI operation.
    /// Combines summary and description fields into a comprehensive description.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to extract from.</param>
    /// <returns>The combined description, or null if no description is available.</returns>
    public string? ExtractDescription(OpenApiOperation operation)
    {
        if (operation is null)
        {
            return null;
        }

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(operation.Summary))
        {
            parts.Add(operation.Summary.Trim());
        }

        if (!string.IsNullOrWhiteSpace(operation.Description))
        {
            parts.Add(operation.Description.Trim());
        }

        return parts.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, parts)
            : null;
    }

    /// <summary>
    /// Converts an OpenAPI schema to a JSON Element.
    /// Handles primitive types, complex objects, arrays, and nested schemas.
    /// Maps OpenAPI data types to JSON Schema format.
    /// </summary>
    /// <param name="schema">The OpenAPI schema to convert.</param>
    /// <returns>JSON Element representing the schema, or null if conversion fails.</returns>
    public JsonElement? ConvertSchemaToJson(JsonSchema schema)
    {
        if (schema is null)
        {
            return null;
        }

        var result = new Dictionary<string, object?>();

        // Handle $ref (reference to another schema)
        if (schema.Reference is not null)
        {
            // Use ReferenceV3 or Id, handling the enum Type
            result["$ref"] = schema.Reference.ReferenceV3; 
            // Fallback if ReferenceV3 is empty (older versions/unresolved)
            if (string.IsNullOrEmpty((string?)result["$ref"]))
            {
                 result["$ref"] = $"#/{schema.Reference.Type}/{schema.Reference.Id}";
            }
        }

        // Handle type
        if (!string.IsNullOrWhiteSpace(schema.Type))
        {
            result["type"] = MapJsonSchemaType(schema.Type);
        }

        // Handle format
        if (!string.IsNullOrWhiteSpace(schema.Format))
        {
            result["format"] = schema.Format;
        }

        // Handle description
        if (!string.IsNullOrWhiteSpace(schema.Description))
        {
            result["description"] = schema.Description;
        }

        // Handle enum values
        if (schema.Enum is { Count: > 0 })
        {
            var enumValues = new List<object?>();
            foreach (var e in schema.Enum)
            {
                if (e is not null)
                {
                    enumValues.Add(GetOpenApiAnyValue(e));
                }
            }
            result["enum"] = enumValues;
        }

        // Handle default value
        if (schema.Default is not null)
        {
            result["default"] = schema.Default;
        }

        // Handle array items
        if (schema.Type == "array" && schema.Items is not null)
        {
            var itemsSchema = ConvertSchemaToJson(schema.Items);
            if (itemsSchema is not null)
            {
                result["items"] = itemsSchema.Value;
            }
        }

        // Handle object properties
        if (schema.Type == "object" && schema.Properties is { Count: > 0 })
        {
            var properties = new Dictionary<string, JsonElement>();
            foreach (var propertyPair in schema.Properties)
            {
                var propertySchema = ConvertSchemaToJson(propertyPair.Value);
                if (propertySchema is not null)
                {
                    properties[propertyPair.Key] = propertySchema.Value;
                }
            }
            result["properties"] = properties;

            // Handle required properties
            if (schema.Required is { Count: > 0 })
            {
                result["required"] = schema.Required;
            }

            // Handle additional properties
            if (schema.AdditionalPropertiesAllowed)
            {
                if (schema.AdditionalProperties is not null)
                {
                    var additionalPropsSchema = ConvertSchemaToJson(schema.AdditionalProperties);
                    if (additionalPropsSchema is not null)
                    {
                        result["additionalProperties"] = additionalPropsSchema.Value;
                    }
                }
                else
                {
                    result["additionalProperties"] = true;
                }
            }
        }

        // Handle allOf (composition)
        if (schema.AllOf is { Count: > 0 })
        {
            var allOfSchemas = new List<JsonElement>();
            foreach (var subSchema in schema.AllOf)
            {
                var converted = ConvertSchemaToJson(subSchema);
                if (converted is not null)
                {
                    allOfSchemas.Add(converted.Value);
                }
            }
            result["allOf"] = allOfSchemas;
        }

        // Handle anyOf (composition)
        if (schema.AnyOf is { Count: > 0 })
        {
            var anyOfSchemas = new List<JsonElement>();
            foreach (var subSchema in schema.AnyOf)
            {
                var converted = ConvertSchemaToJson(subSchema);
                if (converted is not null)
                {
                    anyOfSchemas.Add(converted.Value);
                }
            }
            result["anyOf"] = anyOfSchemas;
        }

        // Handle oneOf (composition)
        if (schema.OneOf is { Count: > 0 })
        {
            var oneOfSchemas = new List<JsonElement>();
            foreach (var subSchema in schema.OneOf)
            {
                var converted = ConvertSchemaToJson(subSchema);
                if (converted is not null)
                {
                    oneOfSchemas.Add(converted.Value);
                }
            }
            result["oneOf"] = oneOfSchemas;
        }

        return JsonSerializer.SerializeToElement(result);
    }

    /// <summary>
    /// Determines if the operation requires authentication based on security requirements.
    /// Analyzes security schemes to infer authentication mode.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to analyze.</param>
    /// <returns>True if the operation requires authentication; false if it allows anonymous access.</returns>
    public bool InferAuthRequirement(OpenApiOperation operation)
    {
        if (operation is null)
        {
            return false;
        }

        // If no security requirements are specified, endpoint is considered public
        if (operation.Security is null || operation.Security.Count == 0)
        {
            return false;
        }

        // Check if there's an empty security requirement (allows anonymous)
        foreach (var securityRequirement in operation.Security)
        {
            if (securityRequirement.Count == 0)
            {
                return false;
            }
        }

        // Security requirements are present, so auth is required
        return true;
    }

    /// <summary>
    /// Extracts the first JSON content from a response.
    /// Prioritizes application/json content type.
    /// </summary>
    /// <param name="response">The OpenAPI response to extract from.</param>
    /// <param name="warnings">Collection to receive warnings.</param>
    /// <returns>The JSON content media type, or null if not found.</returns>
    private static OpenApiMediaType? GetFirstJsonContent(
        IDictionary<string, OpenApiMediaType> content,
        List<string>? warnings = null)
    {
        if (content is null || content.Count == 0)
        {
            return null;
        }

        // Try application/json first
        if (content.TryGetValue("application/json", out var jsonContent))
        {
            return jsonContent;
        }

        // Try any JSON variant
        foreach (var contentPair in content)
        {
            if (contentPair.Key.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                warnings?.Add($"Using {contentPair.Key} instead of application/json");
                return contentPair.Value;
            }
        }

        // Fallback to first available content
        var firstContent = content.Values.FirstOrDefault();
        if (firstContent is not null)
        {
            warnings?.Add($"Using {content.Keys.First()} content type for schema extraction");
        }

        return firstContent;
    }

    /// <summary>
    /// Extracts schema from an OpenAPI response.
    /// Gets the schema from the first JSON content type.
    /// </summary>
    /// <param name="response">The OpenAPI response to extract from.</param>
    /// <param name="warnings">Collection to receive warnings.</param>
    /// <returns>JSON Element representing the response schema.</returns>
    private JsonElement? ExtractSchemaFromResponse(
        OpenApiResponse response,
        List<string> warnings)
    {
        var content = GetFirstJsonContent(response.Content, warnings);
        if (content is null || content.Schema is null)
        {
            return null;
        }

        return ConvertSchemaToJson(content.Schema);
    }

    /// <summary>
    /// Maps OpenAPI data types to JSON Schema types.
    /// Handles OpenAPI-specific type names and converts to standard JSON Schema.
    /// </summary>
    /// <param name="openApiType">The OpenAPI type string.</param>
    /// <returns>The corresponding JSON Schema type.</returns>
    private static string MapJsonSchemaType(string openApiType)
    {
        return openApiType.ToLowerInvariant() switch
        {
            "string" or "str" => "string",
            "integer" or "int" or "int32" or "int64" => "integer",
            "number" or "float" or "double" or "decimal" => "number",
            "boolean" or "bool" => "boolean",
            "array" => "array",
            "object" => "object",
            _ => openApiType
        };
    }

    private static object? GetOpenApiAnyValue(Microsoft.OpenApi.Any.IOpenApiAny any)
    {
        if (any is Microsoft.OpenApi.Any.OpenApiString s) return s.Value;
        if (any is Microsoft.OpenApi.Any.OpenApiInteger i) return i.Value;
        if (any is Microsoft.OpenApi.Any.OpenApiBoolean b) return b.Value;
        if (any is Microsoft.OpenApi.Any.OpenApiDouble d) return d.Value;
        if (any is Microsoft.OpenApi.Any.OpenApiFloat f) return f.Value;
        if (any is Microsoft.OpenApi.Any.OpenApiLong l) return l.Value;
        if (any is Microsoft.OpenApi.Any.OpenApiByte by) return by.Value;
        // Fallback
        return any.ToString();
    }
}
