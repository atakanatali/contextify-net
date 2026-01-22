using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Contextify.Core.JsonSchema;

namespace Contextify.AspNetCore.EndpointParameterBinding;

/// <summary>
/// Service for determining and building unified parameter schemas for ASP.NET Core endpoints.
/// Analyzes route patterns, parameter metadata, and binding attributes to create JSON schemas.
/// Implements conservative inference with warnings for ambiguous or complex binding scenarios.
/// </summary>
public sealed class ContextifyEndpointParameterBinderService : IContextifyEndpointParameterBinderService
{
    private readonly IContextifyJsonSchemaBuilderService _schemaBuilder;
    private readonly ILogger<ContextifyEndpointParameterBinderService> _logger;

    /// <summary>
    /// Initializes a new instance with required dependencies for parameter binding analysis.
    /// </summary>
    /// <param name="schemaBuilder">JSON Schema builder service for type-to-schema conversion.</param>
    /// <param name="logger">Logger for diagnostic and troubleshooting information.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyEndpointParameterBinderService(
        IContextifyJsonSchemaBuilderService schemaBuilder,
        ILogger<ContextifyEndpointParameterBinderService> logger)
    {
        _schemaBuilder = schemaBuilder ?? throw new ArgumentNullException(nameof(schemaBuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Analyzes endpoint parameters and builds a unified input schema combining all parameter sources.
    /// Route parameters become top-level properties, query parameters become properties, and body
    /// becomes a nested "body" property containing its own schema.
    /// </summary>
    /// <param name="endpoint">The endpoint to analyze for parameter binding.</param>
    /// <returns>A parameter binding result containing the unified input schema and any warnings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
    public ContextifyEndpointParameterBindingResultDto BuildParameterBindingSchema(RouteEndpoint endpoint)
    {
        if (endpoint is null)
        {
            throw new ArgumentNullException(nameof(endpoint));
        }

        var warnings = new List<string>();
        var properties = new Dictionary<string, JsonElement>();
        var requiredProperties = new List<string>();

        try
        {
            // Extract route parameters from the route pattern
            var routeParams = ExtractRouteParameters(endpoint);

            // Extract parameter sources from endpoint metadata
            var parameterSources = ExtractParameterSources(endpoint);

            // Group parameters by source type
            var routeParamsMetadata = parameterSources.Where(p => p.SourceType == ContextifyParameterSourceType.Route).ToList();
            var queryParamsMetadata = parameterSources.Where(p => p.SourceType == ContextifyParameterSourceType.Query).ToList();
            var bodyParamsMetadata = parameterSources.Where(p => p.SourceType == ContextifyParameterSourceType.Body).ToList();

            // Detect ambiguous scenarios and add warnings
            if (bodyParamsMetadata.Count > 1)
            {
                warnings.Add("Multiple body parameters detected. ASP.NET Core only supports one body parameter per endpoint.");
            }

            // Process route parameters
            foreach (var param in routeParamsMetadata)
            {
                var schema = GetParameterSchema(param, warnings);
                properties[param.ParameterName] = schema;
                if (param.IsRequired)
                {
                    requiredProperties.Add(param.ParameterName);
                }
            }

            // Also include any route parameters from the template that weren't found in metadata
            foreach (var routeParam in routeParams)
            {
                if (!properties.ContainsKey(routeParam))
                {
                    // Default route parameter schema (string, required)
                    properties[routeParam] = CreatePrimitiveSchema("string", isNullable: false);
                    requiredProperties.Add(routeParam);
                }
            }

            // Process query parameters
            foreach (var param in queryParamsMetadata)
            {
                var schema = GetParameterSchema(param, warnings);
                properties[param.ParameterName] = schema;
                if (param.IsRequired)
                {
                    requiredProperties.Add(param.ParameterName);
                }
            }

            // Process body parameter (only one expected)
            if (bodyParamsMetadata.Count == 1)
            {
                var bodyParam = bodyParamsMetadata[0];
                var bodySchema = GetParameterSchema(bodyParam, warnings);
                properties["body"] = bodySchema;
                if (bodyParam.IsRequired)
                {
                    requiredProperties.Add("body");
                }
            }
            else if (bodyParamsMetadata.Count > 1)
            {
                // Add a generic body schema as fallback
                properties["body"] = CreatePrimitiveSchema("object", isNullable: true);
            }

            // Build the unified schema object
            var unifiedSchema = BuildUnifiedSchema(properties, requiredProperties);

            _logger.LogDebug("Built parameter binding schema for endpoint with {PropertyCount} properties and {WarningCount} warnings",
                properties.Count, warnings.Count);

            return warnings.Count > 0
                ? ContextifyEndpointParameterBindingResultDto.CreateWithWarnings(unifiedSchema, warnings)
                : ContextifyEndpointParameterBindingResultDto.CreateSuccess(unifiedSchema);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build parameter binding schema for endpoint. Returning conservative schema.");
            warnings.Add($"Failed to analyze endpoint parameters: {ex.Message}");
            var conservativeSchema = CreateConservativeSchema();
            return ContextifyEndpointParameterBindingResultDto.CreateWithWarnings(conservativeSchema, warnings);
        }
    }

    /// <summary>
    /// Extracts route parameters from the endpoint route pattern.
    /// Parses parameters enclosed in curly braces with optional constraints.
    /// </summary>
    /// <param name="endpoint">The endpoint to extract route parameters from.</param>
    /// <returns>Read-only list of route parameter names found in the route template.</returns>
    /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
    public IReadOnlyList<string> ExtractRouteParameters(RouteEndpoint endpoint)
    {
        if (endpoint is null)
        {
            throw new ArgumentNullException(nameof(endpoint));
        }

        var routeParams = new List<string>();

        // Try to get route pattern from RouteEndpoint
        if (endpoint is RouteEndpoint routeEndpoint && routeEndpoint.RoutePattern != null)
        {
            foreach (var segment in routeEndpoint.RoutePattern.PathSegments)
            {
                if (segment is RoutePatternPathSegment pathSegment)
                {
                    foreach (var part in pathSegment.Parts)
                    {
                        if (part is RoutePatternParameterPart parameterPart)
                        {
                            routeParams.Add(parameterPart.Name);
                        }
                    }
                }
            }
        }

        return routeParams;
    }

    /// <summary>
    /// Determines the parameter sources for an endpoint by analyzing metadata and signatures.
    /// Uses binding attributes and parameter names to infer ASP.NET Core binding behavior.
    /// </summary>
    /// <param name="endpoint">The endpoint to analyze for parameter sources.</param>
    /// <returns>A read-only list of parameter source descriptors.</returns>
    /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
    public IReadOnlyList<ContextifyParameterSourceDescriptorDto> ExtractParameterSources(RouteEndpoint endpoint)
    {
        if (endpoint is null)
        {
            throw new ArgumentNullException(nameof(endpoint));
        }

        var parameterSources = new List<ContextifyParameterSourceDescriptorDto>();
        var routeParams = ExtractRouteParameters(endpoint).ToHashSet(StringComparer.Ordinal);

        // Try to get parameter information from the endpoint metadata
        var methodInfo = GetEndpointMethodInfo(endpoint);
        if (methodInfo != null)
        {
            foreach (var param in methodInfo.GetParameters())
            {
                var sourceType = DetermineParameterSource(param, routeParams);
                var isRequired = IsParameterRequired(param, sourceType);

                parameterSources.Add(new ContextifyParameterSourceDescriptorDto
                {
                    ParameterName = param.Name ?? "unknown",
                    SourceType = sourceType,
                    ParameterType = param.ParameterType,
                    IsRequired = isRequired
                });
            }
        }

        return parameterSources;
    }

    /// <summary>
    /// Determines the parameter source type based on attributes and parameter characteristics.
    /// Checks for binding attributes and matches parameter names against route templates.
    /// </summary>
    /// <param name="parameter">The parameter to analyze.</param>
    /// <param name="routeParameterNames">Set of route parameter names from the route template.</param>
    /// <returns>The determined parameter source type.</returns>
    private static ContextifyParameterSourceType DetermineParameterSource(
        ParameterInfo parameter,
        HashSet<string> routeParameterNames)
    {
        // Check for explicit binding attributes
        var fromRouteAttribute = parameter.GetCustomAttribute<FromRouteAttribute>();
        if (fromRouteAttribute != null)
        {
            return ContextifyParameterSourceType.Route;
        }

        var fromQueryAttribute = parameter.GetCustomAttribute<FromQueryAttribute>();
        if (fromQueryAttribute != null)
        {
            return ContextifyParameterSourceType.Query;
        }

        var fromBodyAttribute = parameter.GetCustomAttribute<FromBodyAttribute>();
        if (fromBodyAttribute != null)
        {
            return ContextifyParameterSourceType.Body;
        }

        // Check for FromHeader and other attributes
        if (parameter.GetCustomAttribute<FromHeaderAttribute>() != null)
        {
            return ContextifyParameterSourceType.Unknown;
        }

        // Infer based on parameter name matching route
        var paramName = parameter.Name;
        if (paramName != null && routeParameterNames.Contains(paramName))
        {
            return ContextifyParameterSourceType.Route;
        }

        // Infer based on parameter type (complex types default to body in Minimal API)
        var paramType = parameter.ParameterType;
        if (IsComplexType(paramType))
        {
            return ContextifyParameterSourceType.Body;
        }

        // Default: simple types are query parameters
        return ContextifyParameterSourceType.Query;
    }

    /// <summary>
    /// Determines if a parameter is required based on its type and source.
    /// Route parameters are always required. Others depend on nullable annotation.
    /// </summary>
    /// <param name="parameter">The parameter to check.</param>
    /// <param name="sourceType">The determined source type of the parameter.</param>
    /// <returns>True if the parameter is required, false otherwise.</returns>
    private static bool IsParameterRequired(ParameterInfo parameter, ContextifyParameterSourceType sourceType)
    {
        // Route parameters are always required
        if (sourceType == ContextifyParameterSourceType.Route)
        {
            return true;
        }

        // Check nullable value types (int?, DateTime?, etc.)
        var underlyingType = Nullable.GetUnderlyingType(parameter.ParameterType);
        if (underlyingType != null)
        {
            return false; // Nullable value types are optional
        }

        // Check for optional flag
        if (parameter.IsOptional)
        {
            return false;
        }

        // Check for default value
        if (parameter.HasDefaultValue)
        {
            return false;
        }

        // Complex types are typically required (body parameters)
        if (IsComplexType(parameter.ParameterType))
        {
            return true;
        }

        // Value types are required by default
        if (parameter.ParameterType.IsValueType)
        {
            return true;
        }

        // Reference types without nullable annotation are optional (conservative)
        return false;
    }

    /// <summary>
    /// Determines if a type is considered complex (class, record, or collection).
    /// Simple types are primitives, strings, and value types.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is complex, false if simple.</returns>
    private static bool IsComplexType(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) ||
            type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
            type == typeof(Uri) || type == typeof(decimal))
        {
            return false;
        }

        // Nullable value types are not complex
        if (Nullable.GetUnderlyingType(type) != null)
        {
            return false;
        }

        // Classes (but not string) and structs are considered complex
        return type.IsClass || type.IsValueType;
    }

    /// <summary>
    /// Gets the MethodInfo for the endpoint handler if available.
    /// Works with delegate-based endpoints and controller actions.
    /// </summary>
    /// <param name="endpoint">The endpoint to extract method info from.</param>
    /// <returns>The MethodInfo if available, null otherwise.</returns>
    private static MethodInfo? GetEndpointMethodInfo(RouteEndpoint endpoint)
    {
        // Try to get from the endpoint's metadata
        var methodInfo = endpoint.Metadata.GetMetadata<MethodInfo>();
        if (methodInfo != null)
        {
            return methodInfo;
        }

        // Try to get from controller action descriptor
        var actionDescriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (actionDescriptor != null)
        {
            return actionDescriptor.MethodInfo;
        }

        return null;
    }

    /// <summary>
    /// Gets the JSON Schema for a parameter based on its type.
    /// Uses the schema builder service and adds nullable information.
    /// </summary>
    /// <param name="param">The parameter descriptor.</param>
    /// <param name="warnings">List to collect warnings during schema generation.</param>
    /// <returns>JSON Schema element for the parameter type.</returns>
    private JsonElement GetParameterSchema(
        ContextifyParameterSourceDescriptorDto param,
        List<string> warnings)
    {
        try
        {
            if (param.ParameterType != null)
            {
                var schemaJson = _schemaBuilder.BuildJsonSchema(param.ParameterType);
                var schemaElement = JsonDocument.Parse(schemaJson).RootElement;

                // If the parameter is optional, ensure nullable is set
                if (!param.IsRequired && !schemaElement.TryGetProperty("nullable", out _))
                {
                    // Create a modified schema with nullable: true
                    var modifiedSchema = AddNullableToSchema(schemaElement);
                    return modifiedSchema;
                }

                return schemaElement;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate schema for parameter {ParameterName}", param.ParameterName);
            warnings.Add($"Failed to generate schema for parameter '{param.ParameterName}': {ex.Message}");
        }

        // Fallback to generic object schema
        return CreatePrimitiveSchema("object", isNullable: !param.IsRequired);
    }

    /// <summary>
    /// Adds nullable: true to a schema element.
    /// Creates a copy of the schema with the nullable annotation added.
    /// </summary>
    /// <param name="schema">The original schema element.</param>
    /// <returns>A new schema element with nullable: true added.</returns>
    private static JsonElement AddNullableToSchema(JsonElement schema)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new Utf8JsonWriter(memoryStream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();

        foreach (var property in schema.EnumerateObject())
        {
            property.WriteTo(writer);
        }

        writer.WriteBoolean("nullable", true);
        writer.WriteEndObject();
        writer.Flush();

        return JsonDocument.Parse(memoryStream.ToArray()).RootElement;
    }

    /// <summary>
    /// Creates a primitive JSON Schema element for simple types.
    /// Used as fallback when schema generation fails.
    /// </summary>
    /// <param name="type">The JSON Schema type string.</param>
    /// <param name="isNullable">Whether the type is nullable.</param>
    /// <returns>A JSON Schema element representing the primitive type.</returns>
    private static JsonElement CreatePrimitiveSchema(string type, bool isNullable)
    {
        var schema = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonDocument.Parse($"\"{type}\"").RootElement
        };

        if (isNullable)
        {
            schema["nullable"] = JsonDocument.Parse("true").RootElement;
        }

        var jsonString = JsonSerializer.Serialize(schema);
        return JsonDocument.Parse(jsonString).RootElement;
    }

    /// <summary>
    /// Builds the unified schema object from collected properties and required properties.
    /// Combines all parameter sources into a single JSON Schema structure.
    /// </summary>
    /// <param name="properties">Dictionary of property names to their schemas.</param>
    /// <param name="requiredProperties">List of required property names.</param>
    /// <returns>A unified JSON Schema element.</returns>
    private static JsonElement BuildUnifiedSchema(
        Dictionary<string, JsonElement> properties,
        List<string> requiredProperties)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new Utf8JsonWriter(memoryStream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("$schema", "https://json-schema.org/draft/2020-12/schema");
        writer.WriteString("type", "object");

        if (properties.Count > 0)
        {
            writer.WritePropertyName("properties");
            writer.WriteStartObject();

            foreach (var kvp in properties.OrderBy(p => p.Key))
            {
                writer.WritePropertyName(kvp.Key);
                kvp.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        if (requiredProperties.Count > 0)
        {
            writer.WritePropertyName("required");
            writer.WriteStartArray();
            foreach (var required in requiredProperties.OrderBy(r => r))
            {
                writer.WriteStringValue(required);
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.Flush();

        return JsonDocument.Parse(memoryStream.ToArray()).RootElement;
    }

    /// <summary>
    /// Creates a conservative schema that accepts any input.
    /// Used as fallback when endpoint analysis fails.
    /// </summary>
    /// <returns>A permissive JSON Schema element.</returns>
    private static JsonElement CreateConservativeSchema()
    {
        var schema = """
        {
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            "type": "object",
            "properties": {},
            "additionalProperties": true
        }
        """;

        return JsonDocument.Parse(schema).RootElement;
    }
}
