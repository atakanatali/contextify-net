using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contextify.Core.Catalog;
using Contextify.Core.Execution;
using Contextify.Transport.Stdio.JsonRpc.Dto;
using Microsoft.Extensions.Logging;

namespace Contextify.Transport.Stdio.JsonRpc;

/// <summary>
/// Handles MCP JSON-RPC requests over STDIO transport.
/// Routes incoming JSON-RPC requests to the appropriate Contextify catalog and executor services.
/// Provides comprehensive error handling and validation with structured JSON-RPC error responses.
/// </summary>
public sealed class ContextifyStdioJsonRpcHandler : IContextifyStdioJsonRpcHandler
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
    private readonly ILogger<ContextifyStdioJsonRpcHandler> _logger;

    /// <summary>
    /// Initializes a new instance with required dependencies for handling MCP requests.
    /// Uses the catalog provider for tool listing and executor for tool invocation.
    /// </summary>
    /// <param name="catalogProvider">The catalog provider for retrieving available tools.</param>
    /// <param name="toolExecutor">The tool executor for invoking tools.</param>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public ContextifyStdioJsonRpcHandler(
        ContextifyCatalogProviderService catalogProvider,
        IContextifyToolExecutorService toolExecutor,
        ILogger<ContextifyStdioJsonRpcHandler> logger)
    {
        _catalogProvider = catalogProvider ?? throw new ArgumentNullException(nameof(catalogProvider));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
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
    /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
    public async Task<JsonRpcResponseDto> HandleRequestAsync(
        JsonRpcRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug(
            "Processing JSON-RPC request: method={Method}, id={RequestId}",
            request.Method,
            request.RequestId);

        try
        {
            // Validate JSON-RPC version
            if (!IsValidJsonRpcVersion(request.JsonRpcVersion))
            {
                _logger.LogWarning(
                    "Invalid JSON-RPC version: {Version}, expected 2.0",
                    request.JsonRpcVersion);
                return JsonRpcResponseDto.CreateError(
                    InvalidRequestErrorCode,
                    "Invalid JSON-RPC version. Expected '2.0'.",
                    request.RequestId);
            }

            // Route to appropriate method handler
            return request.Method switch
            {
                InitializeMethod => await HandleInitializeAsync(request, cancellationToken),
                ToolsListMethod => await HandleToolsListAsync(request, cancellationToken),
                ToolsCallMethod => await HandleToolsCallAsync(request, cancellationToken),
                _ => HandleMethodNotFound(request)
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "JSON-RPC request cancelled: method={Method}, id={RequestId}",
                request.Method,
                request.RequestId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Internal error processing JSON-RPC request: method={Method}, id={RequestId}",
                request.Method,
                request.RequestId);
            return JsonRpcResponseDto.ErrorWithData(
                InternalErrorCode,
                "Internal error processing request.",
                ex.Message,
                request.RequestId);
        }
    }

    /// <summary>
    /// Handles the initialize method. Returns success acknowledgment.
    /// The initialize method is used to establish the connection between client and server.
    /// </summary>
    /// <param name="request">The JSON-RPC request.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the initialization response.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Task<JsonRpcResponseDto> HandleInitializeAsync(
        JsonRpcRequestDto request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("MCP runtime initialized via JSON-RPC (STDIO)");

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

        return Task.FromResult(JsonRpcResponseDto.Success(result, request.RequestId));
    }

    /// <summary>
    /// Handles the tools/list method. Returns all available tools from the catalog.
    /// Retrieves the current catalog snapshot and converts tool descriptors to MCP format.
    /// </summary>
    /// <param name="request">The JSON-RPC request.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the tools list response.</returns>
    private async Task<JsonRpcResponseDto> HandleToolsListAsync(
        JsonRpcRequestDto request,
        CancellationToken cancellationToken)
    {
        // Ensure we have a fresh catalog snapshot
        var snapshot = await _catalogProvider.EnsureFreshSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);

        var toolsArray = new JsonArray();

        foreach (var toolDescriptor in snapshot.AllTools)
        {
            var toolJson = ConvertToolDescriptorToJson(toolDescriptor);
            toolsArray.Add(toolJson);
        }

        var result = new JsonObject
        {
            ["tools"] = toolsArray
        };

        _logger.LogDebug(
            "Returned {ToolCount} tools in tools/list response",
            snapshot.ToolCount);

        return JsonRpcResponseDto.Success(result, request.RequestId);
    }

    /// <summary>
    /// Handles the tools/call method. Executes the specified tool with provided arguments.
    /// Validates the tool exists, parses arguments, invokes the tool, and returns the result.
    /// </summary>
    /// <param name="request">The JSON-RPC request.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the tool execution response.</returns>
    private async Task<JsonRpcResponseDto> HandleToolsCallAsync(
        JsonRpcRequestDto request,
        CancellationToken cancellationToken)
    {
        // Parse and validate tool call parameters
        if (!TryParseToolCallParams(request.Params, out var toolName, out var arguments, out var paramsError))
        {
            return JsonRpcResponseDto.CreateError(
                InvalidParamsErrorCode,
                paramsError,
                request.RequestId);
        }

        // Get the current catalog snapshot
        var snapshot = _catalogProvider.GetSnapshot();

        // Find the tool descriptor
        if (!snapshot.TryGetTool(toolName, out var toolDescriptor))
        {
            _logger.LogWarning("Tool not found: {ToolName}", toolName);
            return JsonRpcResponseDto.CreateError(
                InvalidParamsErrorCode,
                $"Tool '{toolName}' not found.",
                request.RequestId);
        }

        // Convert JsonObject arguments to dictionary
        var argumentsDict = ConvertJsonArgumentsToDictionary(arguments);

        _logger.LogDebug(
            "Executing tool: {ToolName} with {ArgumentCount} arguments",
            toolName,
            argumentsDict.Count);

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
    /// <param name="request">The JSON-RPC request.</param>
    /// <returns>The method not found error response.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsonRpcResponseDto HandleMethodNotFound(JsonRpcRequestDto request)
    {
        _logger.LogWarning("Unknown JSON-RPC method: {Method}", request.Method);
        return JsonRpcResponseDto.CreateError(
            MethodNotFoundErrorCode,
            $"Method '{request.Method}' not found. Supported methods: initialize, tools/list, tools/call.",
            request.RequestId);
    }

    /// <summary>
    /// Validates that the JSON-RPC version is "2.0".
    /// </summary>
    /// <param name="version">The version string to validate.</param>
    /// <returns>True if the version is valid; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidJsonRpcVersion(string? version)
    {
        return string.Equals(version, "2.0", StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses tool call parameters from the request params object.
    /// Extracts tool name and arguments with proper validation.
    /// </summary>
    /// <param name="paramsObject">The params object from the request.</param>
    /// <param name="toolName">Output parameter for the extracted tool name.</param>
    /// <param name="arguments">Output parameter for the extracted arguments.</param>
    /// <param name="errorMessage">Output parameter for the error message if parsing fails.</param>
    /// <returns>True if parsing succeeded; otherwise, false.</returns>
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
    /// <param name="arguments">The arguments object to convert.</param>
    /// <returns>A dictionary of argument names to values.</returns>
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
    /// <param name="node">The JSON node to convert.</param>
    /// <returns>The converted .NET object value.</returns>
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
    /// <param name="jsonObject">The JSON object to convert.</param>
    /// <returns>A dictionary with key-value pairs from the JSON object.</returns>
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
    /// Converts a Contextify tool descriptor entity to MCP tool descriptor JSON format.
    /// </summary>
    /// <param name="toolDescriptor">The tool descriptor to convert.</param>
    /// <returns>A JsonObject representing the MCP tool descriptor.</returns>
    private static JsonObject ConvertToolDescriptorToJson(ContextifyToolDescriptorEntity toolDescriptor)
    {
        var toolJson = new JsonObject
        {
            ["name"] = toolDescriptor.ToolName
        };

        if (toolDescriptor.Description is not null)
        {
            toolJson["description"] = toolDescriptor.Description;
        }

        if (toolDescriptor.InputSchemaJson.HasValue)
        {
            var schemaText = toolDescriptor.InputSchemaJson.Value.GetRawText();
            toolJson["inputSchema"] = JsonNode.Parse(schemaText);
        }
        else
        {
            toolJson["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            };
        }

        return toolJson;
    }
}
