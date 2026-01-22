using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contextify.Core.Catalog;
using Contextify.Core.Execution;
using Contextify.Transport.Http.Options;
using Contextify.Transport.Http.JsonRpc.Dto;
using Contextify.Transport.Http.Validation;
using Contextify.Mcp.Abstractions.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contextify.Transport.Http.JsonRpc;

/// <summary>
/// Handles MCP JSON-RPC requests over HTTP transport.
/// Routes incoming JSON-RPC requests to the appropriate Contextify catalog and executor services.
/// Provides comprehensive error handling and validation with structured JSON-RPC error responses.
/// Implements security hardening with input validation, size limits, and safe error mapping.
/// </summary>
public sealed partial class ContextifyMcpJsonRpcHandler : IContextifyMcpJsonRpcHandler
{
    // JSON-RPC 2.0 standard error codes
    private const int ParseErrorCode = -32700;
    private const int InvalidRequestErrorCode = -32600;
    private const int MethodNotFoundErrorCode = -32601;
    private const int InvalidParamsErrorCode = -32602;
    private const int InternalErrorCode = -32603;

    // MCP-specific method names
    private const string InitializeMethod = "initialize";
    private const string ToolsListMethod = "tools/list";
    private const string ToolsCallMethod = "tools/call";

    private readonly ContextifyCatalogProviderService _catalogProvider;
    private readonly IContextifyToolExecutorService _toolExecutor;
    private readonly ILogger<ContextifyMcpJsonRpcHandler> _logger;
    private readonly ContextifyInputValidationService _validationService;
    private readonly ContextifyHttpOptions _options;

    /// <summary>
    /// Initializes a new instance with required dependencies for handling MCP requests.
    /// Uses the catalog provider for tool listing, executor for tool invocation, and validation service for security.
    /// </summary>
    /// <param name="catalogProvider">The catalog provider for retrieving available tools.</param>
    /// <param name="toolExecutor">The tool executor for invoking tools.</param>
    /// <param name="validationService">The input validation service for security hardening.</param>
    /// <param name="options">The HTTP transport security options.</param>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public ContextifyMcpJsonRpcHandler(
        ContextifyCatalogProviderService catalogProvider,
        IContextifyToolExecutorService toolExecutor,
        ContextifyInputValidationService validationService,
        IOptions<ContextifyHttpOptions> options,
        ILogger<ContextifyMcpJsonRpcHandler> logger)
    {
        _catalogProvider = catalogProvider ?? throw new ArgumentNullException(nameof(catalogProvider));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
        _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes an incoming JSON-RPC request and returns the appropriate response.
    /// Routes to method-specific handlers based on the requested method name.
    /// All exceptions are caught and converted to JSON-RPC error responses.
    /// </summary>
    /// <param name="request">The JSON-RPC request to process.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the JSON-RPC response.</returns>
    /// <exception cref="ArgumentNullException">Thrown when request    /// <inheritdoc />
    public async Task<JsonRpcResponseDto> HandleRequestAsync(
        JsonRpcRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Log.ProcessingRequest(_logger, request.Method, request.RequestId?.ToString());

        try
        {
            // Validate JSON-RPC version
            if (!IsValidJsonRpcVersion(request.JsonRpcVersion))
            {
                Log.InvalidJsonRpcVersion(_logger, request.JsonRpcVersion);
                return JsonRpcResponseDto.CreateError(
                    InvalidRequestErrorCode,
                    "Invalid JSON-RPC version. Expected '2.0'.",
                    request.RequestId);
            }

            // Route to appropriate method handler
            return request.Method switch
            {
                InitializeMethod => HandleInitialize(request),
                ToolsListMethod => await HandleToolsListAsync(request, cancellationToken),
                ToolsCallMethod => await HandleToolsCallAsync(request, cancellationToken),
                _ => HandleMethodNotFound(request)
            };
        }
        catch (OperationCanceledException)
        {
            Log.RequestCancelled(_logger, request.Method, request.RequestId?.ToString());
            throw;
        }
        catch (Exception ex)
        {
            return CreateSafeErrorResponse(ex, request);
        }
    }

    /// <summary>
    /// Handles the initialize method. Returns success acknowledgment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsonRpcResponseDto HandleInitialize(JsonRpcRequestDto request)
    {
        Log.RuntimeInitialized(_logger);

        var result = new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "Contextify",
                ["version"] = "0.1.0"
            },
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject()
            }
        };

        return JsonRpcResponseDto.Success(result, request.RequestId);
    }

    /// <summary>
    /// Handles the tools/list method. Returns all available tools from the catalog.
    /// Retrieves the current catalog snapshot and converts tool descriptors to MCP format.
    /// </summary>
    private async Task<JsonRpcResponseDto> HandleToolsListAsync(
        JsonRpcRequestDto request,
        CancellationToken cancellationToken)
    {
        // Ensure we have a fresh catalog snapshot
        var snapshot = await _catalogProvider.EnsureFreshSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);

        // Map entities to DTOs
        var toolDtos = snapshot.AllTools.Select(MapToMcpDto).ToList();
        
        // Serialize explicitly using source generation context for potential performance gain if we were returning raw JSON, 
        // but here we wrap in JsonObject for the legacy DTO structure.
        // For cleaner high-perf, we convert the DTO list to JsonNode using the Context.
        var toolsNode = JsonSerializer.SerializeToNode(toolDtos, Serialization.McpJsonContext.Default.ListMcpToolDescriptorDto);

        var result = new JsonObject
        {
            ["tools"] = toolsNode
        };

        Log.ToolsListReturned(_logger, snapshot.ToolCount);

        return JsonRpcResponseDto.Success(result, request.RequestId);
    }

    /// <summary>
    /// Handles the tools/call method. Executes the specified tool with provided arguments.
    /// </summary>
    private async Task<JsonRpcResponseDto> HandleToolsCallAsync(
        JsonRpcRequestDto request,
        CancellationToken cancellationToken)
    {
        // Parse and validate tool call parameters
        if (!TryParseToolCallParams(request.Params, out var toolName, out var arguments, out var paramsError))
        {
            return JsonRpcResponseDto.CreateError(
                _options.InvalidArgumentsErrorCode,
                paramsError,
                request.RequestId);
        }

        // Validate tool name against security rules BEFORE catalog lookup
        if (!_validationService.IsValidToolName(toolName, out var toolNameError))
        {
            Log.ToolNameValidationFailed(_logger, toolName, toolNameError);
            return JsonRpcResponseDto.CreateError(
                _options.InvalidToolNameErrorCode,
                toolNameError ?? "Tool name validation failed.",
                request.RequestId);
        }

        // Validate arguments JSON structure BEFORE execution
        if (!_validationService.IsValidArguments(arguments, out var argumentsError))
        {
            Log.ArgumentsValidationFailed(_logger, toolName, argumentsError);
            return JsonRpcResponseDto.CreateError(
                _options.InvalidArgumentsErrorCode,
                argumentsError ?? "Arguments validation failed.",
                request.RequestId);
        }

        // Get the current catalog snapshot
        var snapshot = _catalogProvider.GetSnapshot();

        // Enforce deny-by-default: check if tool exists in catalog
        if (!snapshot.TryGetTool(toolName, out var toolDescriptor))
        {
            Log.ToolResultPolicyDenied(_logger, toolName);
            return JsonRpcResponseDto.CreateError(
                _options.InvalidToolNameErrorCode,
                _options.EnforceDenyByDefault
                    ? $"Tool '{toolName}' not found or not allowed by policy."
                    : $"Tool '{toolName}' not found.",
                request.RequestId);
        }

        // Convert JsonObject arguments to dictionary
        var argumentsDict = ConvertJsonArgumentsToDictionary(arguments);

        Log.ExecutingTool(_logger, toolName, argumentsDict.Count);

        // Execute the tool
        var result = await _toolExecutor.ExecuteToolAsync(
            toolDescriptor!,
            argumentsDict,
            authContext: null,
            cancellationToken).ConfigureAwait(false);

        // Build the response
        var resultJson = new JsonObject
        {
            ["content"] = result.JsonContent ?? new JsonArray(),
            ["isError"] = !result.IsSuccess
        };

        if (!result.IsSuccess && result.Error?.Message is not null)
        {
            resultJson["error"] = result.Error.Message;
        }

        return JsonRpcResponseDto.Success(resultJson, request.RequestId);
    }

    /// <summary>
    /// Handles requests for unknown methods. Returns method not found error.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsonRpcResponseDto HandleMethodNotFound(JsonRpcRequestDto request)
    {
        Log.UnknownJsonRpcMethod(_logger, request.Method);
        return JsonRpcResponseDto.CreateError(
            MethodNotFoundErrorCode,
            $"Method '{request.Method}' not found. Supported methods: initialize, tools/list, tools/call.",
            request.RequestId);
    }

    /// <summary>
    /// Validates that the JSON-RPC version is "2.0".
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidJsonRpcVersion(string? version)
    {
        return string.Equals(version, "2.0", StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses tool call parameters from the request params object.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Parameter parsing errors are caught and returned as structured errors.")]
    private static bool TryParseToolCallParams(
        object? paramsObject,
        [NotNullWhen(true)] out string? toolName,
        out JsonObject? arguments,
        [NotNullWhen(false)] out string? errorMessage)
    {
        toolName = null;
        arguments = null;
        errorMessage = null;

        if (paramsObject is null)
        {
            errorMessage = "Missing required parameters for tools/call.";
            return false;
        }

        try
        {
            // Handle JsonObject from System.Text.Json.Nodes
            if (paramsObject is JsonObject jsonParams)
            {
                if (!jsonParams.TryGetPropertyValue("name", out var nameNode))
                {
                    errorMessage = "Missing required 'name' parameter in tools/call request.";
                    return false;
                }

                toolName = nameNode?.ToString();

                if (string.IsNullOrWhiteSpace(toolName))
                {
                    errorMessage = "Tool name cannot be empty.";
                    return false;
                }

                jsonParams.TryGetPropertyValue("arguments", out var argsNode);
                arguments = argsNode as JsonObject;
                return true;
            }

            // Handle JsonElement from System.Text.Json
            if (paramsObject is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
            {
                if (!jsonElement.TryGetProperty("name", out var nameProperty))
                {
                    errorMessage = "Missing required 'name' parameter in tools/call request.";
                    return false;
                }

                toolName = nameProperty.GetString();

                if (string.IsNullOrWhiteSpace(toolName))
                {
                    errorMessage = "Tool name cannot be empty.";
                    return false;
                }

                if (jsonElement.TryGetProperty("arguments", out var argsProperty))
                {
                    // Use JsonObject.Create from JsonElement if possible, or Parse
                    // Optimization: avoid double parsing if we can map JsonElement to JsonObject
                    var argsJson = argsProperty.GetRawText();
                    arguments = JsonNode.Parse(argsJson) as JsonObject;
                }

                return true;
            }

            errorMessage = "Invalid parameters format for tools/call request.";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to parse tools/call parameters: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Converts a JsonObject arguments object to a dictionary for tool execution.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Conversion errors are handled gracefully with null values.")]
    private static Dictionary<string, object?> ConvertJsonArgumentsToDictionary(JsonObject? arguments)
    {
        var result = new Dictionary<string, object?>();

        if (arguments is null)
        {
            return result;
        }

        foreach (var property in arguments)
        {
            try
            {
                result[property.Key] = ConvertJsonNodeToObject(property.Value);
            }
            catch
            {
                // Skip invalid arguments
                result[property.Key] = null;
            }
        }

        return result;
    }

    /// <summary>
    /// Converts a JsonNode to a native .NET object for argument passing.
    /// Handles primitive types, arrays, and objects.
    /// </summary>
    private static object? ConvertJsonNodeToObject(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.String => node.ToString(),
            JsonValueKind.Number when node is JsonValue value && value.TryGetValue(out int intVal) => intVal,
            JsonValueKind.Number when node is JsonValue value && value.TryGetValue(out long longVal) => longVal,
            JsonValueKind.Number when node is JsonValue value && value.TryGetValue(out double doubleVal) => doubleVal,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => node.AsArray().Select(ConvertJsonNodeToObject).ToArray(),
            JsonValueKind.Object => ConvertJsonObjectToDictionary(node.AsObject()),
            _ => node.ToString()
        };
    }

    /// <summary>
    /// Converts a JsonObject to a dictionary.
    /// </summary>
    private static Dictionary<string, object?> ConvertJsonObjectToDictionary(JsonObject jsonObject)
    {
        var result = new Dictionary<string, object?>();

        foreach (var property in jsonObject)
        {
            result[property.Key] = ConvertJsonNodeToObject(property.Value);
        }

        return result;
    }

    /// <summary>
    /// Maps a tool descriptor entity to an MCP tool descriptor DTO.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static McpToolDescriptorDto MapToMcpDto(ContextifyToolDescriptorEntity entity)
    {
        return new McpToolDescriptorDto
        {
            Name = entity.ToolName,
            Description = entity.Description,
            InputSchema = entity.InputSchemaJson
        };
    }

    /// <summary>
    /// Creates a safe error response with correlation ID for debugging.
    /// </summary>
    private JsonRpcResponseDto CreateSafeErrorResponse(Exception exception, JsonRpcRequestDto request)
    {
        var correlationId = _options.IncludeCorrelationIdInErrors
            ? Guid.NewGuid().ToString("N")
            : null;

        if (correlationId is not null)
        {
            Log.InternalErrorWithCorrelation(_logger, exception, request.Method, request.RequestId?.ToString(), correlationId);
            
            return JsonRpcResponseDto.CreateErrorWithCorrelation(
                InternalErrorCode,
                "Internal error processing request.",
                correlationId,
                request.RequestId);
        }

        Log.InternalError(_logger, exception, request.Method, request.RequestId?.ToString());

        return JsonRpcResponseDto.CreateError(
            InternalErrorCode,
            "Internal error processing request.",
            request.RequestId);
    }
}
