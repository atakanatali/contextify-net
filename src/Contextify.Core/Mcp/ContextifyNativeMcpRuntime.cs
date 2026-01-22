using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Abstractions.Models;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using Contextify.Core.Execution;
using Contextify.Core.Options;
using Contextify.Core.Pipeline;
using Contextify.Mcp.Abstractions.Dto;
using Contextify.Mcp.Abstractions.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contextify.Core.Mcp;

/// <summary>
/// Native implementation of IMcpRuntime powered by catalog snapshots and action pipeline.
/// Provides tool discovery, invocation, and execution management using the Contextify framework.
/// Uses atomic catalog snapshots for thread-safe tool listing and efficient action pipeline execution.
/// </summary>
/// <remarks>
/// This implementation:
/// - Initializes by building an initial catalog snapshot from policy configuration
/// - Lists tools by reading the atomic snapshot (with optional refresh per request)
/// - Executes tools through the action pipeline with all registered actions (timeout, rate limiting, etc.)
/// - Maps ContextifyToolResultDto to McpToolCallResponseDto for MCP protocol compatibility
///
/// The runtime maintains no shared mutable state per request, using only thread-safe caches
/// (ConcurrentDictionary) and atomic snapshot swapping via Interlocked.Exchange.
/// </remarks>
public sealed class ContextifyNativeMcpRuntime : IMcpRuntime
{
    /// <summary>
    /// Cache for action pipeline executors per effective policy key.
    /// Allows efficient reuse of pipeline instances for tools with identical policy configurations.
    /// Thread-safe ConcurrentDictionary ensures lock-free reads for cached pipelines.
    /// </summary>
    private readonly ConcurrentDictionary<string, ContextifyActionPipelineExecutorService> _pipelineCache;

    /// <summary>
    /// Gets the catalog provider service for atomic snapshot access.
    /// </summary>
    private readonly ContextifyCatalogProviderService _catalogProvider;

    /// <summary>
    /// Gets the tool executor service for final HTTP endpoint invocation.
    /// </summary>
    private readonly IContextifyToolExecutorService _toolExecutor;

    /// <summary>
    /// Gets the runtime options for configuration.
    /// </summary>
    private readonly ContextifyMcpRuntimeOptionsEntity _options;

    /// <summary>
    /// Gets the logger instance for diagnostics and tracing.
    /// </summary>
    private readonly ILogger<ContextifyNativeMcpRuntime> _logger;

    /// <summary>
    /// Gets the service provider for dependency resolution during pipeline execution.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Flag indicating whether InitializeAsync has been called.
    /// Ensures initialization happens exactly once.
    /// </summary>
    private volatile bool _isInitialized;

    /// <summary>
    /// Initializes a new instance with required dependencies for native MCP runtime.
    /// </summary>
    /// <param name="catalogProvider">The catalog provider for atomic snapshot access.</param>
    /// <param name="toolExecutor">The tool executor service for HTTP endpoint invocation.</param>
    /// <param name="options">The runtime options for configuration.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution.</param>
    /// <param name="logger">The logger instance for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public ContextifyNativeMcpRuntime(
        ContextifyCatalogProviderService catalogProvider,
        IContextifyToolExecutorService toolExecutor,
        IOptions<ContextifyMcpRuntimeOptionsEntity> options,
        IServiceProvider serviceProvider,
        ILogger<ContextifyNativeMcpRuntime> logger)
    {
        _catalogProvider = catalogProvider ?? throw new ArgumentNullException(nameof(catalogProvider));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _pipelineCache = new ConcurrentDictionary<string, ContextifyActionPipelineExecutorService>(StringComparer.Ordinal);
        _isInitialized = false;
    }

    /// <summary>
    /// Initializes the native MCP runtime asynchronously.
    /// Ensures the initial catalog snapshot is built and logs the mapping gap report summary.
    /// This method must be called before ListToolsAsync or CallToolAsync to ensure proper initialization.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when initialization fails.</exception>
    /// <remarks>
    /// This method performs the following steps:
    /// 1. Checks if already initialized (idempotent)
    /// 2. Forces a reload of the catalog snapshot to build initial state
    /// 3. Logs tool count and policy source version
    /// 4. Marks the runtime as initialized
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Ensure initialization happens exactly once
        if (_isInitialized)
        {
            _logger.LogDebug("ContextifyNativeMcpRuntime is already initialized. Skipping initialization.");
            return;
        }

        _logger.LogInformation("Initializing ContextifyNativeMcpRuntime...");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Force reload to build initial catalog snapshot
            // This ensures the snapshot is ready for the first ListToolsAsync call
            var snapshot = await _catalogProvider.ReloadAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            // Log the mapping gap report summary
            // This shows how many tools were mapped vs. potential gaps
            LogMappingGapReport(snapshot);

            _logger.LogInformation(
                "ContextifyNativeMcpRuntime initialized successfully. " +
                "Tool count: {ToolCount}, Source version: {SourceVersion}, " +
                "Duration: {DurationMs}ms",
                snapshot.ToolCount,
                snapshot.PolicySourceVersion ?? "none",
                stopwatch.ElapsedMilliseconds);

            // Mark as initialized
            _isInitialized = true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ContextifyNativeMcpRuntime initialization was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to initialize ContextifyNativeMcpRuntime after {DurationMs}ms.", stopwatch.ElapsedMilliseconds);
            throw new InvalidOperationException("Failed to initialize ContextifyNativeMcpRuntime.", ex);
        }
    }

    /// <summary>
    /// Lists all available tools from the catalog snapshot.
    /// Returns tool descriptors containing metadata about each available tool.
    /// Optionally refreshes the snapshot before listing if RefreshPerRequest is enabled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A list of tool descriptors describing available tools.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the runtime is not initialized.</exception>
    /// <remarks>
    /// This method performs the following steps:
    /// 1. Verifies the runtime has been initialized
    /// 2. If RefreshPerRequest is enabled, calls EnsureFreshSnapshotAsync
    /// 3. Reads the atomic snapshot (lock-free via Volatile.Read)
    /// 4. Maps each ContextifyToolDescriptorEntity to McpToolDescriptorDto
    /// 5. Returns the list of tool descriptors
    /// </remarks>
    public async Task<IReadOnlyList<McpToolDescriptorDto>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        // Ensure initialization
        if (!_isInitialized)
        {
            _logger.LogWarning("ListToolsAsync called before initialization. Initializing now.");
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug("Listing tools from catalog snapshot...");

        // Refresh snapshot if configured
        ContextifyToolCatalogSnapshotEntity snapshot;
        if (_options.RefreshPerRequest)
        {
            _logger.LogDebug("RefreshPerRequest is enabled. Ensuring fresh snapshot...");
            snapshot = await _catalogProvider.EnsureFreshSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            snapshot = _catalogProvider.GetSnapshot();
        }

        // Map tool descriptors to MCP format
        var tools = new List<McpToolDescriptorDto>(snapshot.ToolCount);
        foreach (var kvp in snapshot.ToolsByName)
        {
            var tool = kvp.Value;
            var mcpDescriptor = MapToMcpToolDescriptor(tool);
            tools.Add(mcpDescriptor);
        }

        _logger.LogDebug("Listed {ToolCount} tools from catalog snapshot.", tools.Count);

        return tools;
    }

    /// <summary>
    /// Invokes a specific tool with the provided parameters.
    /// Executes the tool through the action pipeline and maps the result to MCP response format.
    /// Resolves the tool descriptor from the snapshot, builds the invocation context, and executes.
    /// </summary>
    /// <param name="request">The tool call request containing tool name and arguments.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The response from the tool execution in MCP format.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the runtime is not initialized.</exception>
    /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
    /// <exception cref="ArgumentException">Thrown when tool name is invalid or not found.</exception>
    /// <remarks>
    /// This method performs the following steps:
    /// 1. Verifies the runtime has been initialized
    /// 2. Validates the request parameters
    /// 3. Resolves the tool descriptor from the catalog snapshot
    /// 4. Builds the ContextifyInvocationContextDto with arguments and services
    /// 5. Gets or creates the pipeline executor for the tool's policy
    /// 6. Executes the tool through the action pipeline
    /// 7. Maps the ContextifyToolResultDto to McpToolCallResponseDto
    ///
    /// The action pipeline executes in order:
    /// - AuthPropagationAction (Order: 90) - Validates auth context propagation
    /// - TimeoutAction (Order: 100) - Enforces timeout limits
    /// - ConcurrencyAction (Order: 110) - Enforces concurrency limits
    /// - RateLimitAction (Order: 120) - Enforces rate limits
    /// - Final: ContextifyToolExecutorService - Executes HTTP endpoint
    /// </remarks>
    public async Task<McpToolCallResponseDto> CallToolAsync(McpToolCallRequestDto request, CancellationToken cancellationToken = default)
    {
        // Ensure initialization
        if (!_isInitialized)
        {
            _logger.LogWarning("CallToolAsync called before initialization. Initializing now.");
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            return new McpToolCallResponseDto
            {
                Content = null,
                IsSuccess = false,
                ErrorMessage = "Tool name cannot be null or whitespace.",
                ErrorType = "INVALID_ARGUMENT"
            };
        }

        _logger.LogDebug("Calling tool '{ToolName}'...", request.ToolName);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Step 1: Resolve tool descriptor from catalog snapshot
            var snapshot = _catalogProvider.GetSnapshot();
            if (!snapshot.TryGetTool(request.ToolName, out var toolDescriptor))
            {
                _logger.LogWarning("Tool '{ToolName}' not found in catalog snapshot.", request.ToolName);
                return new McpToolCallResponseDto
                {
                    Content = null,
                    IsSuccess = false,
                    ErrorMessage = $"Tool '{request.ToolName}' not found or not whitelisted.",
                    ErrorType = "TOOL_NOT_FOUND"
                };
            }

            // Step 2: Build invocation context with arguments, cancellation, services, and effective policy
            var arguments = ConvertArgumentsToDictionary(request.Arguments);
            var invocationContext = new ContextifyInvocationContextDto(
                toolName: request.ToolName,
                arguments: arguments,
                cancellationToken: cancellationToken,
                serviceProvider: _serviceProvider);

            // Step 3: Get or create pipeline executor for this tool's policy
            var pipelineExecutor = GetOrCreatePipelineExecutor(toolDescriptor!);

            // Step 4: Execute tool through action pipeline
            var result = await pipelineExecutor.ExecuteAsync(invocationContext).ConfigureAwait(false);

            stopwatch.Stop();

            // Step 5: Map result to MCP response format
            var response = MapToMcpToolCallResponse(result);

            _logger.LogInformation(
                "Tool '{ToolName}' execution completed in {DurationMs}ms. Success: {IsSuccess}",
                request.ToolName,
                stopwatch.ElapsedMilliseconds,
                response.IsSuccess);

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "Tool '{ToolName}' execution was cancelled after {DurationMs}ms.",
                request.ToolName,
                stopwatch.ElapsedMilliseconds);

            return new McpToolCallResponseDto
            {
                Content = null,
                IsSuccess = false,
                ErrorMessage = "Tool execution was cancelled by client.",
                ErrorType = "CANCELLED"
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Tool '{ToolName}' execution failed after {DurationMs}ms.",
                request.ToolName,
                stopwatch.ElapsedMilliseconds);

            return new McpToolCallResponseDto
            {
                Content = null,
                IsSuccess = false,
                ErrorMessage = $"Tool execution failed: {ex.Message}",
                ErrorType = ex.GetType().Name
            };
        }
    }

    /// <summary>
    /// Converts the JsonObject arguments to a dictionary for the invocation context.
    /// Handles null arguments and converts each property to a dictionary entry.
    /// </summary>
    /// <param name="arguments">The JSON object containing tool arguments.</param>
    /// <returns>A dictionary of argument names to values.</returns>
    /// <remarks>
    /// JSON nodes are converted to their native C# representations:
    /// - JsonValue primitives are converted to string, number, bool
    /// - JsonArray is converted to List<object?>
    /// - JsonObject is converted to Dictionary<string, object?>
    /// </remarks>
    private static IReadOnlyDictionary<string, object?> ConvertArgumentsToDictionary(JsonObject? arguments)
    {
        if (arguments is null)
        {
            return new Dictionary<string, object?>(0, StringComparer.Ordinal);
        }

        var dict = new Dictionary<string, object?>(arguments.Count, StringComparer.Ordinal);

        foreach (var property in arguments)
        {
            dict[property.Key] = ConvertJsonNodeToObject(property.Value);
        }

        return dict;
    }

    /// <summary>
    /// Converts a JsonNode to its native C# object representation.
    /// Recursively handles objects, arrays, and primitive values.
    /// </summary>
    /// <param name="node">The JSON node to convert.</param>
    /// <returns>The converted C# object, or null if node is null.</returns>
    private static object? ConvertJsonNodeToObject(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return node switch
        {
            JsonValue value => value.AsValue(),
            JsonObject obj => obj.ToDictionary(
                kvp => kvp.Key,
                kvp => ConvertJsonNodeToObject(kvp.Value)),
            JsonArray arr => arr.Select(ConvertJsonNodeToObject).ToList(),
            _ => null
        };
    }

    /// <summary>
    /// Maps a ContextifyToolDescriptorEntity to an McpToolDescriptorDto.
    /// Converts the internal tool descriptor format to MCP protocol format.
    /// </summary>
    /// <param name="tool">The tool descriptor to map.</param>
    /// <returns>An MCP tool descriptor with name, description, input schema, and optional metadata.</returns>
    private McpToolDescriptorDto MapToMcpToolDescriptor(ContextifyToolDescriptorEntity tool)
    {
        var descriptor = new McpToolDescriptorDto
        {
            Name = tool.ToolName,
            Description = tool.Description,
            InputSchema = tool.InputSchemaJson
        };

        // Add metadata if enabled
        if (_options.IncludeToolMetadata && tool.EndpointDescriptor is not null)
        {
            var metadata = new Dictionary<string, JsonElement?>
            {
                ["httpMethod"] = JsonSerializer.SerializeToElement(tool.EndpointDescriptor.HttpMethod),
                ["routeTemplate"] = JsonSerializer.SerializeToElement(tool.EndpointDescriptor.RouteTemplate ?? ""),
                ["displayName"] = JsonSerializer.SerializeToElement(tool.EndpointDescriptor.DisplayName ?? ""),
                ["requiresAuth"] = JsonSerializer.SerializeToElement(tool.EndpointDescriptor.RequiresAuth)
            };

            if (tool.EndpointDescriptor.OperationId is not null)
            {
                metadata["operationId"] = JsonSerializer.SerializeToElement(tool.EndpointDescriptor.OperationId);
            }

            // Create JsonElement metadata object
            var metadataJson = JsonSerializer.SerializeToElement(metadata);
            descriptor = descriptor with { Metadata = metadataJson };
        }

        return descriptor;
    }

    /// <summary>
    /// Maps a ContextifyToolResultDto to an McpToolCallResponseDto.
    /// Converts the internal result format to MCP protocol response format.
    /// </summary>
    /// <param name="result">The tool result to map.</param>
    /// <returns>An MCP tool call response with content, success flag, and error information.</returns>
    private static McpToolCallResponseDto MapToMcpToolCallResponse(ContextifyToolResultDto result)
    {
        if (result.IsSuccess)
        {
            // Return successful result with content
            // Prefer JsonContent for structured output, fall back to TextContent
            return new McpToolCallResponseDto
            {
                Content = result.JsonContent ?? (result.TextContent is not null ? JsonValue.Create(result.TextContent) : null),
                IsSuccess = true,
                ErrorMessage = null,
                ErrorType = null
            };
        }

        // Return error result with error information
        return new McpToolCallResponseDto
        {
            Content = null,
            IsSuccess = false,
            ErrorMessage = result.Error?.Message ?? "Unknown error",
            ErrorType = result.Error?.ErrorCode ?? "UNKNOWN"
        };
    }

    /// <summary>
    /// Gets or creates a pipeline executor for the specified tool descriptor.
    /// Caches pipeline executors by effective policy key for efficient reuse.
    /// </summary>
    /// <param name="toolDescriptor">The tool descriptor to get the pipeline for.</param>
    /// <returns>A pipeline executor service configured for the tool's policy.</returns>
    /// <remarks>
    /// Pipeline executors are cached by a key derived from the effective policy.
    /// Tools with identical policy configurations share the same pipeline instance.
    /// This reduces memory overhead and improves performance for tools with similar policies.
    /// </remarks>
    private ContextifyActionPipelineExecutorService GetOrCreatePipelineExecutor(ContextifyToolDescriptorEntity toolDescriptor)
    {
        var policyKey = BuildPolicyKey(toolDescriptor.EffectivePolicy);

        return _pipelineCache.GetOrAdd(policyKey, key =>
        {
            _logger.LogDebug(
                "Creating new pipeline executor for tool '{ToolName}' with policy key '{PolicyKey}'.",
                toolDescriptor.ToolName,
                key);

            // Get all registered actions from the service provider
            var actions = _serviceProvider.GetServices<IContextifyAction>();

            // Create the final executor delegate that calls the tool executor service
            Func<ContextifyInvocationContextDto, ValueTask<ContextifyToolResultDto>> finalExecutor = async ctx =>
            {
                // Resolve the tool descriptor from the current snapshot
                var snapshot = _catalogProvider.GetSnapshot();
                if (!snapshot.TryGetTool(ctx.ToolName, out var currentToolDescriptor))
                {
                    return ContextifyToolResultDto.Failure(
                        "TOOL_NOT_FOUND",
                        $"Tool '{ctx.ToolName}' not found in catalog snapshot.",
                        isTransient: false);
                }

                // Execute the tool via the tool executor service
                return await _toolExecutor.ExecuteToolAsync(
                    currentToolDescriptor!,
                    ctx.Arguments,
                    ctx.AuthContext,
                    ctx.CancellationToken).ConfigureAwait(false);
            };

            // Create the pipeline executor with all actions
            var pipelineLogger = _serviceProvider.GetRequiredService<ILogger<ContextifyActionPipelineExecutorService>>();
            return new ContextifyActionPipelineExecutorService(
                actions,
                finalExecutor,
                pipelineLogger);
        });
    }

    /// <summary>
    /// Builds a cache key for the effective policy.
    /// Creates a string representation of the policy for caching pipeline executors.
    /// </summary>
    /// <param name="policy">The effective policy to build a key for.</param>
    /// <returns>A string key representing the policy configuration.</returns>
    /// <remarks>
    /// The key includes the policy's hash code based on its relevant properties:
    /// - TimeoutMs
    /// - ConcurrencyLimit
    /// - AuthPropagationMode
    /// - RateLimitPolicy (if present)
    ///
    /// This ensures tools with identical policy settings share the same pipeline executor.
    /// </remarks>
    private static string BuildPolicyKey(ContextifyEndpointPolicyDto? policy)
    {
        if (policy is null)
        {
            return "default";
        }

        var hash = new HashCode();
        hash.Add(policy.TimeoutMs ?? 0);
        hash.Add(policy.ConcurrencyLimit ?? 0);
        hash.Add(policy.AuthPropagationMode);
        hash.Add(policy.RateLimitPolicy?.PermitLimit ?? 0);
        hash.Add(policy.RateLimitPolicy?.WindowMs ?? 0);

        return $"policy_{hash.ToHashCode()}";
    }

    /// <summary>
    /// Logs the mapping gap report summary for the catalog snapshot.
    /// Provides diagnostic information about tool mapping coverage.
    /// </summary>
    /// <param name="snapshot">The catalog snapshot to report on.</param>
    private void LogMappingGapReport(ContextifyToolCatalogSnapshotEntity snapshot)
    {
        _logger.LogInformation(
            "Catalog snapshot mapping report: " +
            "Total tools: {ToolCount}, " +
            "Source version: {SourceVersion}, " +
            "Created at: {CreatedUtc}",
            snapshot.ToolCount,
            snapshot.PolicySourceVersion ?? "none",
            snapshot.CreatedUtc);

        if (_options.EnableDetailedDiagnostics)
        {
            foreach (var kvp in snapshot.ToolsByName)
            {
                var tool = kvp.Value;
                _logger.LogDebug(
                    "Tool: {ToolName}, Endpoint: {HasEndpoint}, Policy: {HasPolicy}",
                    tool.ToolName,
                    tool.EndpointDescriptor is not null,
                    tool.EffectivePolicy is not null);
            }
        }
    }
}
