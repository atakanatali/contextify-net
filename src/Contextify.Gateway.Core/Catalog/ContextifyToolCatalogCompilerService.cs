using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Contextify.Config.Abstractions.Policy;
using Contextify.OpenApi.Dto;
using Contextify.Core.Catalog;
using Microsoft.Extensions.Logging;

namespace Contextify.Gateway.Core.Catalog;

/// <summary>
/// Service for compiling endpoint discovery, OpenAPI enrichment, and policy configuration
/// into immutable tool catalog snapshots. Implements policy resolution, tool name generation,
/// description resolution, and schema mapping with comprehensive gap reporting.
/// </summary>
public sealed class ContextifyToolCatalogCompilerService : IContextifyToolCatalogCompilerService
{
    /// <summary>
    /// Gets the logger instance for diagnostics and tracing.
    /// </summary>
    private readonly ILogger<ContextifyToolCatalogCompilerService> _logger;

    /// <summary>
    /// Initializes a new instance with the specified dependencies.
    /// </summary>
    /// <param name="logger">The logger instance for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public ContextifyToolCatalogCompilerService(ILogger<ContextifyToolCatalogCompilerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Compiles discovered endpoints, OpenAPI enrichment data, and policy configuration
    /// into an immutable tool catalog snapshot.
    /// </summary>
    /// <param name="endpoints">The discovered endpoint descriptors to compile.</param>
    /// <param name="openApiEnrichmentData">Optional OpenAPI enrichment data.</param>
    /// <param name="policyConfig">The current policy configuration snapshot.</param>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>A tuple containing the compiled snapshot and gap report.</returns>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public async Task<(ContextifyToolCatalogSnapshotEntity Snapshot, ContextifyMappingGapReportDto GapReport)> CompileAsync(
        IReadOnlyList<ContextifyEndpointDescriptorEntity> endpoints,
        IDictionary<string, ContextifyOpenApiEnrichmentResultDto>? openApiEnrichmentData,
        ContextifyPolicyConfigDto policyConfig,
        CancellationToken ct = default)
    {
        if (endpoints is null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        if (policyConfig is null)
        {
            throw new ArgumentNullException(nameof(policyConfig));
        }

        _logger.LogDebug(
            "Starting tool catalog compilation. Endpoints: {EndpointCount}, " +
            "OpenAPI data available: {HasOpenApiData}, Deny-by-default: {DenyByDefault}",
            endpoints.Count,
            openApiEnrichmentData?.Count ?? 0,
            policyConfig.DenyByDefault);

        var stopwatch = Stopwatch.StartNew();
        var gapReport = new List<string>();
        var tools = new ConcurrentDictionary<string, ContextifyToolDescriptorEntity>(StringComparer.Ordinal);
        var toolNameCollisions = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        // Process endpoints in parallel for better performance with large endpoint collections
        var processingTasks = endpoints.Select(async endpoint =>
        {
            // Check for cancellation
            ct.ThrowIfCancellationRequested();

            await ProcessEndpointAsync(
                endpoint,
                openApiEnrichmentData,
                policyConfig,
                tools,
                toolNameCollisions,
                gapReport,
                ct);
        });

        await Task.WhenAll(processingTasks).ConfigureAwait(false);

        // Build the snapshot with compiled tools
        var snapshot = new ContextifyToolCatalogSnapshotEntity(
            createdUtc: DateTime.UtcNow,
            policySourceVersion: policyConfig.SourceVersion,
            toolsByName: new Dictionary<string, ContextifyToolDescriptorEntity>(tools, StringComparer.Ordinal));

        // Validate the snapshot
        snapshot.Validate();

        stopwatch.Stop();

        _logger.LogInformation(
            "Tool catalog compilation completed. Tools: {ToolCount}, " +
            "Duration: {DurationMs}ms, Warnings: {WarningCount}",
            snapshot.ToolCount,
            stopwatch.ElapsedMilliseconds,
            gapReport.Count);

        // Build the gap report
        var mappingGapReport = BuildGapReport(gapReport, endpoints, openApiEnrichmentData, snapshot);

        return (snapshot, mappingGapReport);
    }

    /// <summary>
    /// Processes a single endpoint and adds it to the tools dictionary if enabled by policy.
    /// </summary>
    /// <param name="endpoint">The endpoint descriptor to process.</param>
    /// <param name="openApiEnrichmentData">Optional OpenAPI enrichment data.</param>
    /// <param name="policyConfig">The policy configuration for resolution.</param>
    /// <param name="tools">The concurrent dictionary to add processed tools to.</param>
    /// <param name="toolNameCollisions">Dictionary tracking tool name collisions.</param>
    /// <param name="gapReport">List to accumulate gap report warnings.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    private Task ProcessEndpointAsync(
        ContextifyEndpointDescriptorEntity endpoint,
        IDictionary<string, ContextifyOpenApiEnrichmentResultDto>? openApiEnrichmentData,
        ContextifyPolicyConfigDto policyConfig,
        ConcurrentDictionary<string, ContextifyToolDescriptorEntity> tools,
        ConcurrentDictionary<string, int> toolNameCollisions,
        List<string> gapReport,
        CancellationToken ct)
    {
        // Step 1: Resolve effective policy for this endpoint
        var effectivePolicy = ResolveEffectivePolicy(endpoint, policyConfig);

        // Step 2: Check if endpoint is enabled by policy
        if (!IsEndpointEnabled(endpoint, effectivePolicy, policyConfig))
        {
            _logger.LogDebug(
                "Endpoint disabled by policy: {HttpMethod} {RouteTemplate}",
                endpoint.HttpMethod ?? "*",
                endpoint.RouteTemplate ?? "(unknown)");

            return Task.CompletedTask;
        }

        // Step 3: Determine tool name (policy override takes precedence)
        string toolName = DetermineToolName(endpoint, effectivePolicy, toolNameCollisions, gapReport);

        // Step 4: Determine description (policy override, then OpenAPI, then fallback)
        string? description = DetermineDescription(
            endpoint,
            effectivePolicy,
            openApiEnrichmentData);

        // Step 5: Determine input schema (prefer OpenAPI, otherwise null)
        JsonElement? inputSchema = DetermineInputSchema(endpoint, openApiEnrichmentData);

        // Step 6: Create the tool descriptor
        var toolDescriptor = new ContextifyToolDescriptorEntity(
            toolName: toolName,
            description: description,
            inputSchemaJson: inputSchema,
            endpointDescriptor: endpoint,
            effectivePolicy: effectivePolicy);

        // Step 7: Add to tools dictionary (handle collisions atomically)
        tools.TryAdd(toolName, toolDescriptor);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves the effective policy for an endpoint based on policy configuration.
    /// Checks blacklist first, then whitelist, then applies deny-by-default rules.
    /// </summary>
    /// <param name="endpoint">The endpoint descriptor to resolve policy for.</param>
    /// <param name="policyConfig">The policy configuration.</param>
    /// <returns>The effective policy for the endpoint, or null if no policy applies.</returns>
    private ContextifyEndpointPolicyDto? ResolveEffectivePolicy(
        ContextifyEndpointDescriptorEntity endpoint,
        ContextifyPolicyConfigDto policyConfig)
    {
        // Check blacklist first (blacklist takes precedence)
        foreach (var blacklistPolicy in policyConfig.Blacklist)
        {
            if (blacklistPolicy.Matches(endpoint.OperationId, endpoint.RouteTemplate, endpoint.HttpMethod, endpoint.DisplayName))
            {
                _logger.LogDebug(
                    "Endpoint matched blacklist policy: {HttpMethod} {RouteTemplate} -> Policy: {PolicyId}",
                    endpoint.HttpMethod ?? "*",
                    endpoint.RouteTemplate ?? "(unknown)",
                    blacklistPolicy.OperationId ?? blacklistPolicy.DisplayName ?? "(unknown)");

                // Return disabled policy for blacklisted endpoints
                return ContextifyEndpointPolicyDto.Disabled(blacklistPolicy.OperationId);
            }
        }

        // Check whitelist
        foreach (var whitelistPolicy in policyConfig.Whitelist)
        {
            if (whitelistPolicy.Matches(endpoint.OperationId, endpoint.RouteTemplate, endpoint.HttpMethod, endpoint.DisplayName))
            {
                _logger.LogDebug(
                    "Endpoint matched whitelist policy: {HttpMethod} {RouteTemplate} -> Policy: {PolicyId}",
                    endpoint.HttpMethod ?? "*",
                    endpoint.RouteTemplate ?? "(unknown)",
                    whitelistPolicy.OperationId ?? whitelistPolicy.DisplayName ?? "(unknown)");

                return whitelistPolicy;
            }
        }

        // No matching policy - deny-by-default determines behavior
        if (policyConfig.DenyByDefault)
        {
            _logger.LogDebug(
                "Endpoint not in policy list and deny-by-default is enabled: {HttpMethod} {RouteTemplate}",
                endpoint.HttpMethod ?? "*",
                endpoint.RouteTemplate ?? "(unknown)");

            return ContextifyEndpointPolicyDto.Disabled(endpoint.OperationId);
        }

        // Allow-by-default: create a default enabled policy
        _logger.LogDebug(
            "Endpoint not in policy list and deny-by-default is disabled: {HttpMethod} {RouteTemplate}",
            endpoint.HttpMethod ?? "*",
            endpoint.RouteTemplate ?? "(unknown)");

        return ContextifyEndpointPolicyDto.DefaultEnabled(
            endpoint.OperationId ?? GenerateFallbackOperationId(endpoint),
            GenerateStableToolName(endpoint.HttpMethod ?? "GET", endpoint.RouteTemplate ?? "unknown"));
    }

    /// <summary>
    /// Determines if an endpoint is enabled based on effective policy and deny-by-default setting.
    /// </summary>
    /// <param name="endpoint">The endpoint descriptor.</param>
    /// <param name="effectivePolicy">The resolved effective policy.</param>
    /// <param name="policyConfig">The policy configuration.</param>
    /// <returns>True if the endpoint is enabled; otherwise, false.</returns>
    private bool IsEndpointEnabled(
        ContextifyEndpointDescriptorEntity endpoint,
        ContextifyEndpointPolicyDto? effectivePolicy,
        ContextifyPolicyConfigDto policyConfig)
    {
        // Null policy means no explicit policy - check deny-by-default
        if (effectivePolicy is null)
        {
            return !policyConfig.DenyByDefault;
        }

        // Use the Enabled property from the effective policy
        return effectivePolicy.Enabled;
    }

    /// <summary>
    /// Determines the tool name for an endpoint based on policy override or stable generation.
    /// Handles collisions by appending a stable hash suffix.
    /// </summary>
    /// <param name="endpoint">The endpoint descriptor.</param>
    /// <param name="effectivePolicy">The resolved effective policy.</param>
    /// <param name="toolNameCollisions">Dictionary tracking name collisions.</param>
    /// <param name="gapReport">List to accumulate gap report warnings.</param>
    /// <returns>The determined tool name.</returns>
    private string DetermineToolName(
        ContextifyEndpointDescriptorEntity endpoint,
        ContextifyEndpointPolicyDto? effectivePolicy,
        ConcurrentDictionary<string, int> toolNameCollisions,
        List<string> gapReport)
    {
        string? baseToolName;

        // Policy override takes precedence
        if (!string.IsNullOrWhiteSpace(effectivePolicy?.ToolName))
        {
            baseToolName = effectivePolicy!.ToolName!;
        }
        else
        {
            // Generate stable default tool name
            baseToolName = GenerateStableToolName(
                endpoint.HttpMethod ?? "GET",
                endpoint.RouteTemplate ?? "unknown");
        }

        // Handle collisions
        var collisionCount = toolNameCollisions.AddOrUpdate(baseToolName, 1, (_, current) => current + 1);

        if (collisionCount > 1)
        {
            // Generate stable hash suffix based on route + method
            var hashSuffix = GenerateStableHashSuffix(endpoint.HttpMethod ?? "GET", endpoint.RouteTemplate ?? "unknown");
            var uniqueToolName = $"{baseToolName}_{hashSuffix}";

            _logger.LogWarning(
                "Tool name collision detected: '{BaseToolName}'. " +
                "Adding stable suffix: '{UniqueToolName}' for {HttpMethod} {RouteTemplate}",
                baseToolName,
                uniqueToolName,
                endpoint.HttpMethod ?? "*",
                endpoint.RouteTemplate ?? "(unknown)");

            gapReport.Add(
                $"Tool name collision: '{baseToolName}' -> '{uniqueToolName}' for " +
                $"{endpoint.HttpMethod ?? "*"} {endpoint.RouteTemplate ?? "(unknown)"}");

            return uniqueToolName;
        }

        return baseToolName;
    }

    /// <summary>
    /// Determines the description for an endpoint based on policy override, OpenAPI, or fallback.
    /// </summary>
    /// <param name="endpoint">The endpoint descriptor.</param>
    /// <param name="effectivePolicy">The resolved effective policy.</param>
    /// <param name="openApiEnrichmentData">Optional OpenAPI enrichment data.</param>
    /// <returns>The determined description, or null if no description is available.</returns>
    private string? DetermineDescription(
        ContextifyEndpointDescriptorEntity endpoint,
        ContextifyEndpointPolicyDto? effectivePolicy,
        IDictionary<string, ContextifyOpenApiEnrichmentResultDto>? openApiEnrichmentData)
    {
        // Policy override takes precedence
        if (!string.IsNullOrWhiteSpace(effectivePolicy?.Description))
        {
            return effectivePolicy!.Description;
        }

        // Try OpenAPI enrichment data
        if (openApiEnrichmentData is not null && endpoint.OperationId is not null)
        {
            if (openApiEnrichmentData.TryGetValue(endpoint.OperationId, out var enrichmentResult))
            {
                if (!string.IsNullOrWhiteSpace(enrichmentResult.Description))
                {
                    return enrichmentResult.Description;
                }
            }
        }

        // Generate fallback description
        if (endpoint.HttpMethod is not null && endpoint.RouteTemplate is not null)
        {
            return GenerateFallbackDescription(endpoint.HttpMethod, endpoint.RouteTemplate);
        }

        return null;
    }

    /// <summary>
    /// Determines the input schema for an endpoint based on OpenAPI enrichment data.
    /// </summary>
    /// <param name="endpoint">The endpoint descriptor.</param>
    /// <param name="openApiEnrichmentData">Optional OpenAPI enrichment data.</param>
    /// <returns>The input schema JSON element, or null if no schema is available.</returns>
    private JsonElement? DetermineInputSchema(
        ContextifyEndpointDescriptorEntity endpoint,
        IDictionary<string, ContextifyOpenApiEnrichmentResultDto>? openApiEnrichmentData)
    {
        // Try OpenAPI enrichment data
        if (openApiEnrichmentData is not null && endpoint.OperationId is not null)
        {
            if (openApiEnrichmentData.TryGetValue(endpoint.OperationId, out var enrichmentResult))
            {
                if (enrichmentResult.InputSchemaJson.HasValue)
                {
                    return enrichmentResult.InputSchemaJson;
                }
            }
        }

        // No schema available from OpenAPI
        // TODO: Implement reflection-based schema builder as fallback
        return null;
    }

    /// <summary>
    /// Generates a stable tool name based on HTTP method and route template.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (e.g., GET, POST).</param>
    /// <param name="routeTemplate">The route template (e.g., api/tools/{id}).</param>
    /// <returns>A stable tool name suitable for catalog inclusion.</returns>
    public string GenerateStableToolName(string httpMethod, string routeTemplate)
    {
        if (string.IsNullOrWhiteSpace(httpMethod))
        {
            httpMethod = "GET";
        }

        if (string.IsNullOrWhiteSpace(routeTemplate))
        {
            routeTemplate = "unknown";
        }

        // Normalize the route template
        var normalizedRoute = NormalizeRouteTemplate(routeTemplate);

        // Combine method and route
        var toolName = $"{httpMethod}_{normalizedRoute}";

        return toolName;
    }

    /// <summary>
    /// Generates a fallback description for an endpoint.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (e.g., GET, POST).</param>
    /// <param name="routeTemplate">The route template (e.g., api/tools/{id}).</param>
    /// <returns>A generated description suitable for tool discovery.</returns>
    public string GenerateFallbackDescription(string httpMethod, string routeTemplate)
    {
        if (string.IsNullOrWhiteSpace(httpMethod))
        {
            httpMethod = "GET";
        }

        if (string.IsNullOrWhiteSpace(routeTemplate))
        {
            routeTemplate = "unknown";
        }

        return $"Execute {httpMethod} request on {routeTemplate}";
    }

    /// <summary>
    /// Normalizes a route template for use in tool names.
    /// Removes parameter markers, trims slashes, and replaces special characters.
    /// </summary>
    /// <param name="routeTemplate">The route template to normalize.</param>
    /// <returns>The normalized route template.</returns>
    private static string NormalizeRouteTemplate(string routeTemplate)
    {
        // Remove leading/trailing slashes
        var normalized = routeTemplate.Trim('/');

        // Replace parameter markers {param} and {param:constraint}
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\{[^}]+:\w+\}", m =>
        {
            // Extract just the parameter name
            var match = m.Value;
            var colonIndex = match.IndexOf(':');
            return colonIndex > 0 ? match[..colonIndex] : match;
        });

        // Replace curly braces with underscores
        normalized = normalized.Replace("{", "_").Replace("}", "_");

        // Replace multiple consecutive underscores with single underscore
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"_+", "_");

        // Replace remaining special characters with underscores
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^a-zA-Z0-9_-]", "_");

        // Trim trailing underscores
        normalized = normalized.Trim('_');

        // If empty after normalization, use "unknown"
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "unknown";
        }

        return normalized;
    }

    /// <summary>
    /// Generates a stable hash suffix for handling tool name collisions.
    /// The hash is deterministic based on the HTTP method and route template.
    /// </summary>
    /// <param name="httpMethod">The HTTP method.</param>
    /// <param name="routeTemplate">The route template.</param>
    /// <returns>A stable hash suffix string.</returns>
    private static string GenerateStableHashSuffix(string httpMethod, string routeTemplate)
    {
        // Combine method and route for hashing
        var input = $"{httpMethod}:{routeTemplate}";

        // Compute stable hash
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);

        // Use first 8 characters of hex hash (32 bits)
        var hashHex = Convert.ToHexString(hash)[..8].ToLowerInvariant();

        return hashHex;
    }

    /// <summary>
    /// Generates a fallback operation ID for endpoints without one.
    /// </summary>
    /// <param name="endpoint">The endpoint descriptor.</param>
    /// <returns>A generated operation ID.</returns>
    private static string GenerateFallbackOperationId(ContextifyEndpointDescriptorEntity endpoint)
    {
        var method = endpoint.HttpMethod ?? "GET";
        var route = endpoint.RouteTemplate ?? "unknown";

        return $"{method}_{route}".Replace("/", "_").Replace("{", "_").Replace("}", "_");
    }

    /// <summary>
    /// Builds a mapping gap report from accumulated warnings and endpoint data.
    /// </summary>
    /// <param name="gapReportWarnings">List of accumulated gap report warnings.</param>
    /// <param name="endpoints">All endpoints that were processed.</param>
    /// <param name="openApiEnrichmentData">OpenAPI enrichment data that was used.</param>
    /// <param name="snapshot">The compiled snapshot.</param>
    /// <returns>A mapping gap report DTO.</returns>
    private static ContextifyMappingGapReportDto BuildGapReport(
        List<string> gapReportWarnings,
        IReadOnlyList<ContextifyEndpointDescriptorEntity> endpoints,
        IDictionary<string, ContextifyOpenApiEnrichmentResultDto>? openApiEnrichmentData,
        ContextifyToolCatalogSnapshotEntity snapshot)
    {
        var unmatchedEndpoints = new List<ContextifyUnmatchedEndpointDto>();
        var missingRequestSchemas = new List<ContextifyMissingSchemaDto>();

        // Find endpoints without OpenAPI enrichment (if OpenAPI was provided)
        if (openApiEnrichmentData is not null)
        {
            foreach (var endpoint in endpoints)
            {
                if (endpoint.OperationId is not null)
                {
                    if (!openApiEnrichmentData.ContainsKey(endpoint.OperationId))
                    {
                        unmatchedEndpoints.Add(new ContextifyUnmatchedEndpointDto
                        {
                            RouteTemplate = endpoint.RouteTemplate,
                            HttpMethod = endpoint.HttpMethod,
                            OperationId = endpoint.OperationId,
                            DisplayName = endpoint.DisplayName
                        });
                    }
                }
            }

            // Find tools without input schemas
            foreach (var tool in snapshot.AllTools)
            {
                if (!tool.InputSchemaJson.HasValue &&
                    tool.EndpointDescriptor?.OperationId is not null)
                {
                    missingRequestSchemas.Add(new ContextifyMissingSchemaDto
                    {
                        OperationId = tool.EndpointDescriptor.OperationId,
                        RouteTemplate = tool.EndpointDescriptor.RouteTemplate,
                        HttpMethod = tool.EndpointDescriptor.HttpMethod
                    });
                }
            }
        }

        return new ContextifyMappingGapReportDto
        {
            UnmatchedEndpoints = unmatchedEndpoints.AsReadOnly(),
            MissingRequestSchemas = missingRequestSchemas.AsReadOnly(),
            MissingResponseSchemas = [],
            UnknownAuthInference = [],
            GeneralWarnings = gapReportWarnings.AsReadOnly()
        };
    }
}
