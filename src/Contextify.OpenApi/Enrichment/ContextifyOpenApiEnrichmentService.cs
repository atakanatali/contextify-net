using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Contextify.Core.Catalog;
using Contextify.OpenApi.Dto;

namespace Contextify.OpenApi.Enrichment;

/// <summary>
/// Service for enriching tool descriptors with OpenAPI/Swagger metadata.
/// Detects OpenAPI document availability, matches endpoints to operations,
/// and extracts schemas and descriptions for tool enrichment.
/// </summary>
public sealed class ContextifyOpenApiEnrichmentService : IContextifyOpenApiEnrichmentService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOpenApiOperationMatcher _operationMatcher;
    private readonly IOpenApiSchemaExtractor _schemaExtractor;
    private readonly ILogger<ContextifyOpenApiEnrichmentService> _logger;
    private readonly object _lock = new();
    private OpenApiDocument? _cachedDocument;
    private bool _availabilityChecked;
    private bool _isAvailable;

    /// <summary>
    /// Initializes a new instance with required dependencies for OpenAPI enrichment.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving ApiExplorer and Swagger services.</param>
    /// <param name="operationMatcher">Service for matching endpoints to OpenAPI operations.</param>
    /// <param name="schemaExtractor">Service for extracting schemas from OpenAPI operations.</param>
    /// <param name="logger">Logger for diagnostic and troubleshooting information.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyOpenApiEnrichmentService(
        IServiceProvider serviceProvider,
        IOpenApiOperationMatcher operationMatcher,
        IOpenApiSchemaExtractor schemaExtractor,
        ILogger<ContextifyOpenApiEnrichmentService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _operationMatcher = operationMatcher ?? throw new ArgumentNullException(nameof(operationMatcher));
        _schemaExtractor = schemaExtractor ?? throw new ArgumentNullException(nameof(schemaExtractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Enriches a collection of tool descriptors with OpenAPI metadata.
    /// Matches endpoints to OpenAPI operations and extracts schemas and descriptions.
    /// </summary>
    /// <param name="toolDescriptors">The tool descriptors to enrich.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A tuple containing the enriched tool descriptors and a mapping gap report.</returns>
    public async Task<(IReadOnlyList<ContextifyToolDescriptorEntity> EnrichedDescriptors, ContextifyMappingGapReportDto GapReport)>
        EnrichToolsAsync(
            IReadOnlyList<ContextifyToolDescriptorEntity> toolDescriptors,
            CancellationToken cancellationToken = default)
    {
        if (toolDescriptors is null || toolDescriptors.Count == 0)
        {
            return (Array.Empty<ContextifyToolDescriptorEntity>(), ContextifyMappingGapReportDto.Empty());
        }

        // Ensure OpenAPI document is loaded
        var document = await GetOpenApiDocumentAsync(cancellationToken);
        if (document is null)
        {
            _logger.LogWarning("OpenAPI document not available for enrichment");
            return (toolDescriptors, ContextifyMappingGapReportDto.Empty());
        }

        var enrichedDescriptors = new List<ContextifyToolDescriptorEntity>(toolDescriptors.Count);
        var unmatchedEndpoints = new List<ContextifyUnmatchedEndpointDto>();
        var missingRequestSchemas = new List<ContextifyMissingSchemaDto>();
        var missingResponseSchemas = new List<ContextifyMissingSchemaDto>();
        var unknownAuthInference = new List<ContextifyAuthInferenceWarningDto>();
        var generalWarnings = new List<string>();

        foreach (var descriptor in toolDescriptors)
        {
            var enrichmentResult = await EnrichToolAsync(descriptor, cancellationToken);

            if (!enrichmentResult.IsEnriched)
            {
                // Track unmatched endpoint
                if (descriptor.EndpointDescriptor is not null)
                {
                    unmatchedEndpoints.Add(new ContextifyUnmatchedEndpointDto
                    {
                        RouteTemplate = descriptor.EndpointDescriptor.RouteTemplate,
                        HttpMethod = descriptor.EndpointDescriptor.HttpMethod,
                        OperationId = descriptor.EndpointDescriptor.OperationId,
                        DisplayName = descriptor.EndpointDescriptor.DisplayName
                    });
                }
                enrichedDescriptors.Add(descriptor);
                continue;
            }

            // Create enriched descriptor
            var enrichedDescriptor = CreateEnrichedDescriptor(
                descriptor,
                enrichmentResult);

            enrichedDescriptors.Add(enrichedDescriptor);

            // Collect warnings for gap report
            CollectSchemaWarnings(
                descriptor,
                enrichmentResult,
                missingRequestSchemas,
                missingResponseSchemas,
                unknownAuthInference);
        }

        // Build gap report
        var gapReport = new ContextifyMappingGapReportDto
        {
            UnmatchedEndpoints = unmatchedEndpoints.AsReadOnly(),
            MissingRequestSchemas = missingRequestSchemas.AsReadOnly(),
            MissingResponseSchemas = missingResponseSchemas.AsReadOnly(),
            UnknownAuthInference = unknownAuthInference.AsReadOnly(),
            GeneralWarnings = generalWarnings.AsReadOnly()
        };

        return (enrichedDescriptors, gapReport);
    }

    /// <summary>
    /// Enriches a single tool descriptor with OpenAPI metadata.
    /// Matches the endpoint to an OpenAPI operation and extracts schemas and descriptions.
    /// </summary>
    /// <param name="toolDescriptor">The tool descriptor to enrich.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The enrichment result with updated metadata.</returns>
    public Task<ContextifyOpenApiEnrichmentResultDto> EnrichToolAsync(
        ContextifyToolDescriptorEntity toolDescriptor,
        CancellationToken cancellationToken = default)
    {
        if (toolDescriptor is null)
        {
            return Task.FromResult(ContextifyOpenApiEnrichmentResultDto.NotEnriched());
        }

        var endpointDescriptor = toolDescriptor.EndpointDescriptor;
        if (endpointDescriptor is null)
        {
            _logger.LogDebug("Tool {ToolName} has no endpoint descriptor, skipping enrichment",
                toolDescriptor.ToolName);
            return Task.FromResult(ContextifyOpenApiEnrichmentResultDto.NotEnriched());
        }

        // Match to OpenAPI operation
        var operation = _operationMatcher.MatchOperation(
            endpointDescriptor.RouteTemplate,
            endpointDescriptor.HttpMethod,
            endpointDescriptor.OperationId,
            endpointDescriptor.DisplayName);

        if (operation is null)
        {
            _logger.LogDebug(
                "No matching OpenAPI operation found for endpoint {RouteTemplate} {HttpMethod}",
                endpointDescriptor.RouteTemplate, endpointDescriptor.HttpMethod);
            return Task.FromResult(ContextifyOpenApiEnrichmentResultDto.NotEnriched());
        }

        // Extract metadata from operation
        var warnings = new List<string>();
        var description = _schemaExtractor.ExtractDescription(operation);
        var inputSchema = _schemaExtractor.ExtractInputSchema(operation);
        var responseSchema = _schemaExtractor.ExtractResponseSchema(operation, warnings);

        // Get matched operation ID
        var matchedOperationId = GetOperationIdFromMatchedOperation(operation, endpointDescriptor);

        if (warnings.Count > 0)
        {
            return Task.FromResult(ContextifyOpenApiEnrichmentResultDto.PartialEnrichment(
                description,
                inputSchema,
                responseSchema,
                matchedOperationId ?? string.Empty,
                warnings));
        }

        return Task.FromResult(ContextifyOpenApiEnrichmentResultDto.Enriched(
            description,
            inputSchema,
            responseSchema,
            matchedOperationId));
    }

    /// <summary>
    /// Detects whether OpenAPI/Swagger is available in the current application.
    /// Checks for registered ApiExplorer or Swagger providers.
    /// </summary>
    /// <returns>True if OpenAPI is available; otherwise, false.</returns>
    public bool IsOpenApiAvailable()
    {
        if (_availabilityChecked)
        {
            return _isAvailable;
        }

        lock (_lock)
        {
            if (_availabilityChecked)
            {
                return _isAvailable;
            }

            // Check for ApiExplorer
            var apiExplorer = _serviceProvider.GetService<IApiDescriptionGroupCollectionProvider>();
            if (apiExplorer is not null)
            {
                _logger.LogDebug("ApiExplorer is available for OpenAPI enrichment");
                _isAvailable = true;
                _availabilityChecked = true;
                return true;
            }

            // Check for Swagger provider
            var swaggerProviderType = Type.GetType("Swashbuckle.AspNetCore.Swagger.ISwaggerProvider, Swashbuckle.AspNetCore.Swagger");
            if (swaggerProviderType is not null)
            {
                var swaggerProvider = _serviceProvider.GetService(swaggerProviderType);
                if (swaggerProvider is not null)
                {
                    _logger.LogDebug("Swagger provider is available for OpenAPI enrichment");
                    _isAvailable = true;
                    _availabilityChecked = true;
                    return true;
                }
            }

            _logger.LogDebug("No OpenAPI/Swagger provider detected");
            _isAvailable = false;
            _availabilityChecked = true;
            return false;
        }
    }

    /// <summary>
    /// Generates a mapping gap report for the given tool descriptors.
    /// Identifies endpoints without OpenAPI matches and missing schemas.
    /// </summary>
    /// <param name="toolDescriptors">The tool descriptors to analyze.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A mapping gap report with diagnostic information.</returns>
    public async Task<ContextifyMappingGapReportDto> GenerateGapReportAsync(
        IReadOnlyList<ContextifyToolDescriptorEntity> toolDescriptors,
        CancellationToken cancellationToken = default)
    {
        if (toolDescriptors is null || toolDescriptors.Count == 0)
        {
            return ContextifyMappingGapReportDto.Empty();
        }

        var document = await GetOpenApiDocumentAsync(cancellationToken);
        if (document is null)
        {
            _logger.LogWarning("OpenAPI document not available for gap report generation");
            return new ContextifyMappingGapReportDto
            {
                GeneralWarnings = new List<string> { "OpenAPI document not available" }.AsReadOnly()
            };
        }

        var unmatchedEndpoints = new List<ContextifyUnmatchedEndpointDto>();
        var missingRequestSchemas = new List<ContextifyMissingSchemaDto>();
        var missingResponseSchemas = new List<ContextifyMissingSchemaDto>();
        var unknownAuthInference = new List<ContextifyAuthInferenceWarningDto>();

        foreach (var descriptor in toolDescriptors)
        {
            var endpointDescriptor = descriptor.EndpointDescriptor;
            if (endpointDescriptor is null)
            {
                continue;
            }

            var operation = _operationMatcher.MatchOperation(
                endpointDescriptor.RouteTemplate,
                endpointDescriptor.HttpMethod,
                endpointDescriptor.OperationId,
                endpointDescriptor.DisplayName);

            if (operation is null)
            {
                unmatchedEndpoints.Add(new ContextifyUnmatchedEndpointDto
                {
                    RouteTemplate = endpointDescriptor.RouteTemplate,
                    HttpMethod = endpointDescriptor.HttpMethod,
                    OperationId = endpointDescriptor.OperationId,
                    DisplayName = endpointDescriptor.DisplayName
                });
                continue;
            }

            // Check for missing request schema
            var requestSchema = _schemaExtractor.ExtractInputSchema(operation);
            if (requestSchema is null && HasRequestBody(operation))
            {
                var operationId = GetOperationIdFromMatchedOperation(operation, endpointDescriptor);
                missingRequestSchemas.Add(new ContextifyMissingSchemaDto
                {
                    OperationId = operationId,
                    RouteTemplate = endpointDescriptor.RouteTemplate,
                    HttpMethod = endpointDescriptor.HttpMethod
                });
            }

            // Check for missing response schema
            var responseWarnings = new List<string>();
            var responseSchema = _schemaExtractor.ExtractResponseSchema(operation, responseWarnings);
            if (responseSchema is null)
            {
                var operationId = GetOperationIdFromMatchedOperation(operation, endpointDescriptor);
                missingResponseSchemas.Add(new ContextifyMissingSchemaDto
                {
                    OperationId = operationId,
                    RouteTemplate = endpointDescriptor.RouteTemplate,
                    HttpMethod = endpointDescriptor.HttpMethod,
                    StatusCode = "2xx"
                });
            }
        }

        return new ContextifyMappingGapReportDto
        {
            UnmatchedEndpoints = unmatchedEndpoints.AsReadOnly(),
            MissingRequestSchemas = missingRequestSchemas.AsReadOnly(),
            MissingResponseSchemas = missingResponseSchemas.AsReadOnly(),
            UnknownAuthInference = unknownAuthInference.AsReadOnly(),
            GeneralWarnings = Array.Empty<string>().AsReadOnly()
        };
    }

    /// <summary>
    /// Gets or loads the OpenAPI document for enrichment operations.
    /// Caches the document for subsequent calls to avoid repeated loading.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The OpenAPI document, or null if not available.</returns>
    /// <summary>
    /// Gets or loads the OpenAPI document for enrichment operations.
    /// Caches the document for subsequent calls to avoid repeated loading.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The OpenAPI document, or null if not available.</returns>
    private Task<OpenApiDocument?> GetOpenApiDocumentAsync(CancellationToken cancellationToken)
    {
        if (!IsOpenApiAvailable())
        {
            return Task.FromResult<OpenApiDocument?>(null);
        }

        if (_cachedDocument is not null)
        {
            return Task.FromResult<OpenApiDocument?>(_cachedDocument);
        }

        lock (_lock)
        {
            if (_cachedDocument is not null)
            {
                return Task.FromResult<OpenApiDocument?>(_cachedDocument);
            }

            // Try to get document from Swagger provider
            var document = LoadDocumentFromSwaggerProvider();
            if (document is not null)
            {
                _cachedDocument = document;
                return Task.FromResult<OpenApiDocument?>(_cachedDocument);
            }

            return Task.FromResult<OpenApiDocument?>(null);
        }
    }

    /// <summary>
    /// Loads OpenAPI document from the registered Swagger provider.
    /// Uses reflection to avoid hard dependency on Swashbuckle.
    /// </summary>
    /// <returns>The OpenAPI document, or null if not available.</returns>
    [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
        Justification = "Reflection requires abstract types for dynamic loading")]
    private OpenApiDocument? LoadDocumentFromSwaggerProvider()
    {
        try
        {
            var swaggerProviderType = Type.GetType("Swashbuckle.AspNetCore.Swagger.ISwaggerProvider, Swashbuckle.AspNetCore.Swagger");
            if (swaggerProviderType is null)
            {
                return null;
            }

            var swaggerProvider = _serviceProvider.GetService(swaggerProviderType);
            if (swaggerProvider is null)
            {
                return null;
            }

            // GetSwagger method typically takes document name parameter
            var getSwaggerMethod = swaggerProviderType.GetMethod("GetSwagger", new[] { typeof(string), typeof(string), typeof(string) });
            if (getSwaggerMethod is null)
            {
                _logger.LogWarning("Could not find GetSwagger method on Swagger provider");
                return null;
            }

            // Default to "v1" if not specified, as Swashbuckle requires a document name
            var document = getSwaggerMethod.Invoke(swaggerProvider, new object?[] { "v1", null, null }) as OpenApiDocument;
            if (document is not null)
            {
                _logger.LogDebug("Successfully loaded OpenAPI document with {PathCount} paths",
                    document.Paths.Count);
            }

            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading OpenAPI document from Swagger provider");
            return null;
        }
    }

    /// <summary>
    /// Creates an enriched tool descriptor from the original and enrichment result.
    /// Preserves original properties while updating description and input schema.
    /// </summary>
    /// <param name="original">The original tool descriptor.</param>
    /// <param name="enrichmentResult">The enrichment result with new metadata.</param>
    /// <returns>A new enriched tool descriptor entity.</returns>
    private static ContextifyToolDescriptorEntity CreateEnrichedDescriptor(
        ContextifyToolDescriptorEntity original,
        ContextifyOpenApiEnrichmentResultDto enrichmentResult)
    {
        // Use enriched description if available, otherwise keep original
        var description = enrichmentResult.Description ?? original.Description;

        // Use enriched input schema if available, otherwise keep original
        var inputSchemaJson = enrichmentResult.InputSchemaJson ?? original.InputSchemaJson;

        return new ContextifyToolDescriptorEntity(
            toolName: original.ToolName,
            description: description,
            inputSchemaJson: inputSchemaJson,
            endpointDescriptor: original.EndpointDescriptor?.DeepCopy(),
            effectivePolicy: original.EffectivePolicy);
    }

    /// <summary>
    /// Collects schema-related warnings for the gap report.
    /// Analyzes enrichment results to identify missing schemas and auth inference issues.
    /// </summary>
    private void CollectSchemaWarnings(
        ContextifyToolDescriptorEntity descriptor,
        ContextifyOpenApiEnrichmentResultDto enrichmentResult,
        List<ContextifyMissingSchemaDto> missingRequestSchemas,
        List<ContextifyMissingSchemaDto> missingResponseSchemas,
        List<ContextifyAuthInferenceWarningDto> unknownAuthInference)
    {
        var endpoint = descriptor.EndpointDescriptor;
        if (endpoint is null)
        {
            return;
        }

        // Check for missing request schema
        if (enrichmentResult.InputSchemaJson is null && endpoint.Consumes.Count > 0)
        {
            missingRequestSchemas.Add(new ContextifyMissingSchemaDto
            {
                OperationId = enrichmentResult.MatchedOperationId,
                RouteTemplate = endpoint.RouteTemplate,
                HttpMethod = endpoint.HttpMethod,
                ContentType = endpoint.Consumes.FirstOrDefault()
            });
        }

        // Check for missing response schema
        if (enrichmentResult.ResponseSchemaJson is null && endpoint.Produces.Count > 0)
        {
            missingResponseSchemas.Add(new ContextifyMissingSchemaDto
            {
                OperationId = enrichmentResult.MatchedOperationId,
                RouteTemplate = endpoint.RouteTemplate,
                HttpMethod = endpoint.HttpMethod,
                StatusCode = "200",
                ContentType = endpoint.Produces.FirstOrDefault()
            });
        }
    }

    /// <summary>
    /// Gets the operation ID from the matched OpenAPI operation.
    /// Falls back to the endpoint operation ID if the operation doesn't have one.
    /// </summary>
    /// <param name="operation">The matched OpenAPI operation.</param>
    /// <param name="endpointDescriptor">The endpoint descriptor.</param>
    /// <returns>The operation ID, or null if not available.</returns>
    private static string? GetOperationIdFromMatchedOperation(
        OpenApiOperation operation,
        ContextifyEndpointDescriptorEntity endpointDescriptor)
    {
        if (!string.IsNullOrWhiteSpace(operation.OperationId))
        {
            return operation.OperationId;
        }

        return endpointDescriptor.OperationId;
    }

    /// <summary>
    /// Determines if the operation has a request body defined.
    /// Checks for presence of request body in the OpenAPI operation.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to check.</param>
    /// <returns>True if the operation has a request body; false otherwise.</returns>
    private static bool HasRequestBody(OpenApiOperation operation)
    {
        return operation.RequestBody is not null;
    }
}
