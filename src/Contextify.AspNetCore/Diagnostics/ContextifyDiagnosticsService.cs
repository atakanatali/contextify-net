using System.Reflection;
using Contextify.AspNetCore.Diagnostics.Dto;
using Contextify.AspNetCore.EndpointDiscovery;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using Microsoft.Extensions.Logging;

namespace Contextify.AspNetCore.Diagnostics;

/// <summary>
/// Default implementation of IContextifyDiagnosticsService.
/// Generates manifest and diagnostics information by analyzing catalog snapshot,
/// policy configuration, and discovered endpoints.
/// Thread-safe for concurrent access in production environments.
/// </summary>
public sealed class ContextifyDiagnosticsService : IContextifyDiagnosticsService
{
    /// <summary>
    /// Gets the logger instance for diagnostics and tracing.
    /// </summary>
    private readonly ILogger<ContextifyDiagnosticsService> _logger;

    /// <summary>
    /// Gets the catalog provider service for accessing the current tool catalog snapshot.
    /// </summary>
    private readonly ContextifyCatalogProviderService _catalogProvider;

    /// <summary>
    /// Gets the policy configuration provider for accessing policy settings.
    /// </summary>
    private readonly IContextifyPolicyConfigProvider _policyConfigProvider;

    /// <summary>
    /// Gets the endpoint discovery service for discovering registered endpoints.
    /// </summary>
    private readonly IContextifyEndpointDiscoveryService _endpointDiscoveryService;

    /// <summary>
    /// Gets the assembly version for the Contextify package.
    /// Cached after first retrieval for performance.
    /// </summary>
    private readonly Lazy<string> _assemblyVersion;

    /// <summary>
    /// Initializes a new instance with the specified dependencies.
    /// </summary>
    /// <param name="catalogProvider">The catalog provider service.</param>
    /// <param name="policyConfigProvider">The policy configuration provider.</param>
    /// <param name="endpointDiscoveryService">The endpoint discovery service.</param>
    /// <param name="logger">The logger instance for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyDiagnosticsService(
        ContextifyCatalogProviderService catalogProvider,
        IContextifyPolicyConfigProvider policyConfigProvider,
        IContextifyEndpointDiscoveryService endpointDiscoveryService,
        ILogger<ContextifyDiagnosticsService> logger)
    {
        _catalogProvider = catalogProvider ?? throw new ArgumentNullException(nameof(catalogProvider));
        _policyConfigProvider = policyConfigProvider ?? throw new ArgumentNullException(nameof(policyConfigProvider));
        _endpointDiscoveryService = endpointDiscoveryService ?? throw new ArgumentNullException(nameof(endpointDiscoveryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Cache assembly version for performance
        _assemblyVersion = new Lazy<string>(() =>
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var assemblyVersion = informationalVersion
                               ?? assembly.GetName().Version?.ToString()
                               ?? "unknown";
            return assemblyVersion;
        });
    }

    /// <summary>
    /// Generates the Contextify service manifest.
    /// Returns service metadata for discovery and compatibility verification.
    /// Does not leak sensitive policy details by design.
    /// </summary>
    /// <param name="mcpHttpEndpoint">The MCP HTTP endpoint path if mapped.</param>
    /// <param name="openApiAvailable">Whether OpenAPI/Swagger is available.</param>
    /// <param name="serviceName">Optional service name override. Uses assembly name if null.</param>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// The task result contains the manifest DTO with service metadata.
    /// </returns>
    public async Task<ContextifyManifestDto> GenerateManifestAsync(
        string? mcpHttpEndpoint,
        bool openApiAvailable,
        string? serviceName = null,
        CancellationToken ct = default)
    {
        // Get the current catalog snapshot
        var snapshot = _catalogProvider.GetSnapshot();

        // Use provided service name or fall back to assembly name
        var resolvedServiceName = !string.IsNullOrWhiteSpace(serviceName)
            ? serviceName!
            : GetDefaultServiceName();

        var manifest = new ContextifyManifestDto
        {
            ServiceName = resolvedServiceName,
            Version = _assemblyVersion.Value,
            McpHttpEndpoint = mcpHttpEndpoint,
            ToolCount = snapshot.ToolCount,
            PolicySourceVersion = snapshot.PolicySourceVersion,
            LastCatalogBuildUtc = snapshot.CreatedUtc,
            OpenApiAvailable = openApiAvailable
        };

        _logger.LogDebug(
            "Generated manifest: ServiceName={ServiceName}, Version={Version}, ToolCount={ToolCount}, " +
            "PolicySourceVersion={PolicySourceVersion}, McpHttpEndpoint={McpHttpEndpoint}",
            manifest.ServiceName,
            manifest.Version,
            manifest.ToolCount,
            manifest.PolicySourceVersion ?? "none",
            manifest.McpHttpEndpoint ?? "none");

        return await Task.FromResult(manifest).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates comprehensive diagnostics information.
    /// Includes mapping gaps between policy and discovered endpoints, plus tool summaries.
    /// Useful for troubleshooting and operational monitoring.
    /// </summary>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// The task result contains the diagnostics DTO with gaps and tool summaries.
    /// </returns>
    public async Task<ContextifyDiagnosticsDto> GenerateDiagnosticsAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Generating diagnostics at {Timestamp}", DateTime.UtcNow);

        // Get current state
        var snapshot = _catalogProvider.GetSnapshot();
        var policyConfig = await _policyConfigProvider.GetAsync(ct).ConfigureAwait(false);
        var discoveredEndpoints = await _endpointDiscoveryService.DiscoverEndpointsAsync().ConfigureAwait(false);

        // Analyze mapping gaps
        var mappingGaps = AnalyzeMappingGaps(policyConfig, discoveredEndpoints, snapshot);

        // Build tool summaries
        var toolSummaries = BuildToolSummaries(snapshot);

        var diagnostics = new ContextifyDiagnosticsDto
        {
            TimestampUtc = DateTime.UtcNow,
            MappingGaps = mappingGaps,
            EnabledTools = toolSummaries
        };

        _logger.LogInformation(
            "Generated diagnostics: ToolCount={ToolCount}, GapCount={GapCount}",
            diagnostics.EnabledToolCount,
            diagnostics.MappingGaps.Count);

        return diagnostics;
    }

    /// <summary>
    /// Analyzes mapping gaps between policy configuration and discovered endpoints.
    /// Identifies whitelisted tools without corresponding endpoints.
    /// </summary>
    /// <param name="policyConfig">The policy configuration.</param>
    /// <param name="discoveredEndpoints">The discovered endpoints.</param>
    /// <param name="snapshot">The current catalog snapshot.</param>
    /// <returns>A list of mapping gap warnings.</returns>
    private static List<ContextifyMappingGapWarningDto> AnalyzeMappingGaps(
        ContextifyPolicyConfigDto policyConfig,
        IReadOnlyList<ContextifyEndpointDescriptorEntity> discoveredEndpoints,
        ContextifyToolCatalogSnapshotEntity snapshot)
    {
        var gaps = new List<ContextifyMappingGapWarningDto>();

        // Build lookup for discovered endpoints
        var endpointLookup = new Dictionary<string, ContextifyEndpointDescriptorEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in discoveredEndpoints)
        {
            var key = $"{endpoint.HttpMethod}:{endpoint.RouteTemplate}";
            endpointLookup[key] = endpoint;
        }

        // Check each whitelisted policy
        foreach (var policy in policyConfig.Whitelist)
        {
            // Skip disabled policies
            if (!policy.Enabled)
            {
                continue;
            }

            // Skip policies without route template
            if (string.IsNullOrWhiteSpace(policy.RouteTemplate))
            {
                gaps.Add(new ContextifyMappingGapWarningDto(
                    gapType: "RouteNotSpecified",
                    toolName: policy.ToolName,
                    expectedRoute: null,
                    httpMethod: policy.HttpMethod,
                    description: $"Whitelisted tool '{policy.ToolName ?? "(unknown)"}' does not specify a route template.",
                    severity: "Warning"));
                continue;
            }

            // Check if endpoint exists
            var method = policy.HttpMethod ?? "GET";
            var key = $"{method}:{policy.RouteTemplate}";

            if (!endpointLookup.TryGetValue(key, out var discoveredEndpoint))
            {
                gaps.Add(new ContextifyMappingGapWarningDto(
                    gapType: "EndpointNotFound",
                    toolName: policy.ToolName,
                    expectedRoute: policy.RouteTemplate,
                    httpMethod: policy.HttpMethod,
                    description: $"Whitelisted tool '{policy.ToolName ?? "(unknown)"}' references route '{policy.RouteTemplate}' " +
                                 $"with method '{method}' but no matching endpoint was discovered.",
                    severity: "Error"));
            }
        }

        return gaps;
    }

    /// <summary>
    /// Builds lightweight tool summaries from the catalog snapshot.
    /// Provides summary information without exposing full policy details.
    /// </summary>
    /// <param name="snapshot">The catalog snapshot.</param>
    /// <returns>A list of tool summary DTOs.</returns>
    private static List<ContextifyToolSummaryDto> BuildToolSummaries(ContextifyToolCatalogSnapshotEntity snapshot)
    {
        var summaries = new List<ContextifyToolSummaryDto>(snapshot.ToolCount);

        foreach (var kvp in snapshot.ToolsByName)
        {
            var tool = kvp.Value;
            var endpoint = tool.EndpointDescriptor;
            summaries.Add(new ContextifyToolSummaryDto(
                toolName: tool.ToolName,
                description: tool.Description,
                routeTemplate: endpoint?.RouteTemplate ?? string.Empty,
                httpMethod: endpoint?.HttpMethod ?? string.Empty,
                requiresAuth: endpoint?.RequiresAuth ?? false,
                displayName: endpoint?.DisplayName));
        }

        // Sort by tool name for deterministic output
        summaries.Sort((a, b) => string.CompareOrdinal(a.ToolName, b.ToolName));

        return summaries;
    }

    /// <summary>
    /// Gets the default service name from the entry assembly.
    /// Falls back to "Contextify" if assembly name cannot be determined.
    /// </summary>
    /// <returns>The default service name.</returns>
    private static string GetDefaultServiceName()
    {
        try
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly is not null)
            {
                return entryAssembly.GetName().Name ?? "Contextify";
            }

            var executingAssembly = Assembly.GetExecutingAssembly();
            return executingAssembly.GetName().Name ?? "Contextify";
        }
        catch
        {
            return "Contextify";
        }
    }
}
