using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contextify.Actions.Abstractions.Models;
using Contextify.Core.Catalog;
using Contextify.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contextify.Core.Execution;

/// <summary>
/// Service for executing Contextify tools through their configured HTTP endpoints.
/// Supports in-process HTTP execution with IHttpClientFactory for optimal performance and reliability.
/// Provides comprehensive request building, response parsing, and error handling with full async/await support.
/// </summary>
public sealed class ContextifyToolExecutorService : IContextifyToolExecutorService
{
    private const string JsonContentType = "application/json";
    private const int DefaultMaxRedirects = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ContextifyToolExecutorOptionsEntity _options;
    private readonly ILogger<ContextifyToolExecutorService> _logger;

    /// <summary>
    /// Initializes a new instance with required dependencies for tool execution.
    /// Uses IHttpClientFactory for creating HTTP clients and options for configuration.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory for creating configured HTTP clients.</param>
    /// <param name="options">The tool executor options for configuration.</param>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public ContextifyToolExecutorService(
        IHttpClientFactory httpClientFactory,
        IOptions<ContextifyToolExecutorOptionsEntity> options,
        ILogger<ContextifyToolExecutorService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a tool by invoking its configured endpoint with the provided arguments.
    /// Handles request building, HTTP communication, and response parsing based on execution mode.
    /// </summary>
    /// <param name="toolDescriptor">The descriptor of the tool to execute.</param>
    /// <param name="arguments">The arguments to pass to the tool.</param>
    /// <param name="authContext">Optional authentication context for security headers.</param>
    /// <param name="cancellationToken">The cancellation token for cancelling the operation.</param>
    /// <returns>A task yielding a tool result with response data or error information.</returns>
    /// <exception cref="ArgumentNullException">Thrown when toolDescriptor is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when endpoint descriptor is null.</exception>
    public async Task<ContextifyToolResultDto> ExecuteToolAsync(
        ContextifyToolDescriptorEntity toolDescriptor,
        IReadOnlyDictionary<string, object?> arguments,
        ContextifyAuthContextDto? authContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toolDescriptor);
        ArgumentNullException.ThrowIfNull(arguments);

        if (toolDescriptor.EndpointDescriptor is null)
        {
            _logger.LogError(
                "Tool '{ToolName}' has no configured endpoint for execution.",
                toolDescriptor.ToolName);
            return ContextifyToolResultDto.Failure(
                "NO_ENDPOINT",
                $"Tool '{toolDescriptor.ToolName}' has no configured endpoint.",
                isTransient: false);
        }

        var endpoint = toolDescriptor.EndpointDescriptor;

        try
        {
            var httpClient = CreateHttpClient();

            var requestUri = BuildUri(endpoint, arguments);
            var (httpMethod, requestContent) = BuildRequestBody(endpoint, arguments);

            _logger.LogDebug(
                "Executing tool '{ToolName}' via {HttpMethod} {RequestUri}",
                toolDescriptor.ToolName,
                httpMethod,
                requestUri);

            using var request = new HttpRequestMessage(httpMethod, requestUri)
            {
                Content = requestContent
            };

            // Apply authentication context if provided and enabled
            if (authContext is not null && _options.PropagateAuthContext)
            {
                authContext.ApplyToHttpRequest(request);
            }

            // Add diagnostic headers if enabled
            if (_options.IncludeDiagnosticHeaders)
            {
                request.Headers.TryAddWithoutValidation("X-Contextify-Tool-Name", toolDescriptor.ToolName);
                request.Headers.TryAddWithoutValidation("X-Contextify-Execution-Mode", _options.ExecutionMode.Name);
            }

            // Apply timeout from policy or use default
            var timeout = GetTimeout(toolDescriptor);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                linkedCts.Token).ConfigureAwait(false);

            return await ParseResponseAsync(response, toolDescriptor.ToolName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Tool '{ToolName}' execution timed out after {TimeoutMs}ms",
                toolDescriptor.ToolName,
                GetTimeout(toolDescriptor));
            return ContextifyToolResultDto.Failure(
                "TIMEOUT",
                $"Tool execution timed out after {GetTimeout(toolDescriptor)}ms.",
                isTransient: true);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                ex,
                "Tool '{ToolName}' execution was cancelled by client",
                toolDescriptor.ToolName);
            return ContextifyToolResultDto.Failure(
                "CANCELLED",
                "Tool execution was cancelled by client.",
                isTransient: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP request failed for tool '{ToolName}': {Message}",
                toolDescriptor.ToolName,
                ex.Message);
            return ContextifyToolResultDto.Failure(
                "HTTP_ERROR",
                $"HTTP request failed: {ex.Message}",
                isTransient: true);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "JSON parsing failed for tool '{ToolName}': {Message}",
                toolDescriptor.ToolName,
                ex.Message);
            return ContextifyToolResultDto.Failure(
                "JSON_PARSE_ERROR",
                $"Failed to parse response: {ex.Message}",
                isTransient: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error executing tool '{ToolName}': {Message}",
                toolDescriptor.ToolName,
                ex.Message);
            return ContextifyToolResultDto.FromException(ex, isTransient: false);
        }
    }

    /// <summary>
    /// Creates an HTTP client based on the configured execution mode.
    /// Uses the named client from IHttpClientFactory for optimal connection pooling and configuration.
    /// </summary>
    /// <returns>An HTTP client instance ready for making requests.</returns>
    private HttpClient CreateHttpClient()
    {
        if (_options.ExecutionMode == ContextifyExecutionMode.InProcessHttp)
        {
            return _httpClientFactory.CreateClient(_options.HttpClientName);
        }

        // Remote execution will be supported in future versions
        throw new InvalidOperationException(
            $"Execution mode '{_options.ExecutionMode.Name}' is not yet supported.");
    }

    /// <summary>
    /// Gets the timeout in milliseconds for the tool execution.
    /// Uses the policy timeout if available, otherwise falls back to the configured default.
    /// </summary>
    /// <param name="toolDescriptor">The tool descriptor containing policy information.</param>
    /// <returns>The timeout in milliseconds.</returns>
    private int GetTimeout(ContextifyToolDescriptorEntity toolDescriptor)
    {
        if (toolDescriptor.EffectivePolicy?.TimeoutMs.HasValue == true)
        {
            return toolDescriptor.EffectivePolicy.TimeoutMs.Value;
        }

        return _options.DefaultTimeoutSeconds * 1000;
    }

    /// <summary>
    /// Builds the request URI by expanding the route template with route parameters from arguments.
    /// Route parameters are identified by placeholders in the template (e.g., {id}, {userId}).
    /// Remaining arguments are added as query string parameters.
    /// </summary>
    /// <param name="endpoint">The endpoint descriptor containing the route template.</param>
    /// <param name="arguments">The arguments to extract route and query parameters from.</param>
    /// <returns>The fully built request URI string.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "URI building errors are handled gracefully with logging.")]
    private string BuildUri(
        ContextifyEndpointDescriptorEntity endpoint,
        IReadOnlyDictionary<string, object?> arguments)
    {
        if (string.IsNullOrWhiteSpace(endpoint.RouteTemplate))
        {
            return string.Empty;
        }

        try
        {
            var routeTemplate = endpoint.RouteTemplate!;
            var routeBuilder = new StringBuilder(routeTemplate.Length);
            var queryParams = new List<string>();

            // Track used arguments to distinguish route from query parameters
            var usedArgumentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int i = 0;
            while (i < routeTemplate.Length)
            {
                // Check for route parameter placeholder {paramName}
                if (routeTemplate[i] == '{')
                {
                    var closingBraceIndex = routeTemplate.IndexOf('}', i);
                    if (closingBraceIndex > i + 1)
                    {
                        var paramName = routeTemplate.Substring(i + 1, closingBraceIndex - i - 1);

                        // Try to find a matching argument (case-insensitive)
                        var argumentKey = arguments.Keys.FirstOrDefault(k =>
                            string.Equals(k, paramName, StringComparison.OrdinalIgnoreCase));

                        if (argumentKey is not null && arguments.TryGetValue(argumentKey, out var value))
                        {
                            var stringValue = ConvertToString(value);
                            routeBuilder.Append(Uri.EscapeDataString(stringValue ?? string.Empty));
                            usedArgumentKeys.Add(argumentKey);
                        }
                        else
                        {
                            // Keep the placeholder if no matching argument found
                            routeBuilder.Append(routeTemplate.Substring(i, closingBraceIndex - i + 1));
                        }

                        i = closingBraceIndex + 1;
                        continue;
                    }
                }

                routeBuilder.Append(routeTemplate[i]);
                i++;
            }

            // Build query string from unused arguments
            foreach (var kvp in arguments)
            {
                if (!usedArgumentKeys.Contains(kvp.Key) && kvp.Key != "body")
                {
                    var stringValue = ConvertToString(kvp.Value);
                    if (stringValue is not null)
                    {
                        queryParams.Add($"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(stringValue)}");
                    }
                }
            }

            var resultUri = routeBuilder.ToString();
            if (queryParams.Count > 0)
            {
                resultUri += '?' + string.Join('&', queryParams);
            }

            return resultUri;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build URI from template '{RouteTemplate}'", endpoint.RouteTemplate);
            return endpoint.RouteTemplate ?? string.Empty;
        }
    }

    /// <summary>
    /// Builds the HTTP request body content from the arguments.
    /// Only processes the "body" argument if present, serializing it as JSON.
    /// </summary>
    /// <param name="endpoint">The endpoint descriptor for method information.</param>
    /// <param name="arguments">The arguments containing the potential body content.</param>
    /// <returns>A tuple of HTTP method and optional HTTP content for the request body.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Body building errors are handled gracefully with logging.")]
    private (System.Net.Http.HttpMethod Method, HttpContent? Content) BuildRequestBody(
        ContextifyEndpointDescriptorEntity endpoint,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var httpMethod = GetHttpMethod(endpoint.HttpMethod);
        HttpContent? content = null;

        // Only build body for methods that typically have a body
        if (ShouldHaveRequestBody(httpMethod) &&
            arguments.TryGetValue("body", out var bodyValue) &&
            bodyValue is not null)
        {
            try
            {
                var json = JsonSerializer.Serialize(bodyValue, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                content = new StringContent(json, Encoding.UTF8, JsonContentType);

                // Validate content length against maximum
                if (_options.MaxRequestContentLengthBytes > 0)
                {
                    var contentLength = Encoding.UTF8.GetByteCount(json);
                    if (contentLength > _options.MaxRequestContentLengthBytes)
                    {
                        _logger.LogWarning(
                            "Request body size ({ContentLength} bytes) exceeds maximum {MaxContentLength} bytes",
                            contentLength,
                            _options.MaxRequestContentLengthBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to serialize request body");
            }
        }

        return (httpMethod, content);
    }

    /// <summary>
    /// Parses the HTTP response into a tool result based on content type.
    /// Handles JSON responses with structured parsing and text responses with direct content extraction.
    /// Non-success status codes are converted to error results.
    /// </summary>
    /// <param name="response">The HTTP response to parse.</param>
    /// <param name="toolName">The name of the tool for logging purposes.</param>
    /// <param name="cancellationToken">The cancellation token for async operations.</param>
    /// <returns>A task yielding the parsed tool result.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Response parsing errors are caught and converted to error results.")]
    private async Task<ContextifyToolResultDto> ParseResponseAsync(
        HttpResponseMessage response,
        string toolName,
        CancellationToken cancellationToken)
    {
        try
        {
            var contentType = response.Content.Headers.ContentType?.MediaType;

            // Handle non-success status codes
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await TryReadResponseContentAsync(response, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogWarning(
                    "Tool '{ToolName}' returned non-success status {StatusCode}: {Content}",
                    toolName,
                    response.StatusCode,
                    errorContent);

                return ContextifyToolResultDto.Failure(
                    $"HTTP_{(int)response.StatusCode}",
                    $"Tool execution failed with HTTP {response.StatusCode}: {errorContent}",
                    isTransient: IsTransientStatusCode(response.StatusCode));
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            // Parse based on content type
            if (IsJsonContentType(contentType))
            {
                return await ParseJsonResponseAsync(responseContent, contentType)
                    .ConfigureAwait(false);
            }

            // Default to text response
            return ContextifyToolResultDto.Success(responseContent, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse response for tool '{ToolName}'", toolName);
            return ContextifyToolResultDto.Failure(
                "RESPONSE_PARSE_ERROR",
                $"Failed to parse response: {ex.Message}",
                isTransient: false);
        }
    }

    /// <summary>
    /// Parses a JSON response string into a structured tool result.
    /// Attempts to parse as JsonNode for structured access with a text summary fallback.
    /// </summary>
    /// <param name="jsonContent">The JSON string content from the response.</param>
    /// <param name="contentType">The content type header value.</param>
    /// <returns>A task yielding the parsed tool result with JSON content.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<ContextifyToolResultDto> ParseJsonResponseAsync(
        string jsonContent,
        string? contentType)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var document = JsonDocument.Parse(jsonContent);
                var jsonNode = JsonNode.Parse(jsonContent);

                // Create both structured JSON and text summary
                var textSummary = CreateTextSummaryFromJson(document.RootElement);

                return new ContextifyToolResultDto(
                    textContent: textSummary,
                    jsonContent: jsonNode,
                    contentType: contentType ?? JsonContentType);
            }
            catch (JsonException)
            {
                // If JSON parsing fails, return as plain text
                return ContextifyToolResultDto.Success(jsonContent, contentType);
            }
        }, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a human-readable text summary from a JSON element.
    /// Extracts key values from objects or formats arrays as readable text.
    /// </summary>
    /// <param name="element">The JSON element to summarize.</param>
    /// <returns>A human-readable text summary of the JSON content.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Summary generation failures gracefully fall back to raw text.")]
    private static string? CreateTextSummaryFromJson(JsonElement element)
    {
        try
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => CreateObjectSummary(element),
                JsonValueKind.Array => CreateArraySummary(element),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l.ToString() : element.GetDouble().ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }
        catch
        {
            return element.ToString();
        }
    }

    /// <summary>
    /// Creates a summary string from a JSON object element.
    /// Formats key-value pairs in a readable "key: value" list format.
    /// </summary>
    /// <param name="obj">The JSON object element to summarize.</param>
    /// <returns>A formatted string of key-value pairs.</returns>
    private static string CreateObjectSummary(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var summary = new StringBuilder();
        var first = true;

        foreach (var property in obj.EnumerateObject())
        {
            if (!first)
            {
                summary.Append(", ");
            }
            first = false;

            summary.Append(property.Name);
            summary.Append(": ");
            summary.Append(CreateTextSummaryFromJson(property.Value) ?? "null");
        }

        return summary.ToString();
    }

    /// <summary>
    /// Creates a summary string from a JSON array element.
    /// Formats array items as a bracketed list with comma separation.
    /// </summary>
    /// <param name="array">The JSON array element to summarize.</param>
    /// <returns>A formatted string of array items.</returns>
    private static string CreateArraySummary(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var items = array.EnumerateArray()
            .Select(e => CreateTextSummaryFromJson(e) ?? "null")
            .Take(10) // Limit to first 10 items for summary
            .ToList();

        return $"[{string.Join(", ", items)}{(items.Count < array.GetArrayLength() ? ", ..." : "")}]";
    }

    /// <summary>
    /// Attempts to read response content with error handling.
    /// Returns empty string on failure to prevent cascading errors.
    /// </summary>
    /// <param name="response">The HTTP response to read from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task yielding the response content or empty string on failure.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async Task<string> TryReadResponseContentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Converts an object value to its string representation for use in URI building.
    /// Handles null values, primitives, and provides fallback for complex types.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The string representation of the value, or null if conversion is not possible.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Conversion failures return null rather than throwing.")]
    private static string? ConvertToString(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            bool b => b.ToString().ToLowerInvariant(),
            DateTime dt => dt.ToString("O"), // ISO 8601
            DateTimeOffset dto => dto.ToString("O"),
            Guid g => g.ToString(),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    /// <summary>
    /// Gets the HTTP method enum from a string method name.
    /// Supports common HTTP methods with fallback to GET for unknown values.
    /// </summary>
    /// <param name="method">The HTTP method string (e.g., "GET", "POST", "PUT").</param>
    /// <returns>The corresponding HttpMethod instance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static System.Net.Http.HttpMethod GetHttpMethod(string? method)
    {
        return method?.ToUpperInvariant() switch
        {
            "GET" => System.Net.Http.HttpMethod.Get,
            "POST" => System.Net.Http.HttpMethod.Post,
            "PUT" => System.Net.Http.HttpMethod.Put,
            "DELETE" => System.Net.Http.HttpMethod.Delete,
            "PATCH" => System.Net.Http.HttpMethod.Patch,
            "HEAD" => System.Net.Http.HttpMethod.Head,
            "OPTIONS" => System.Net.Http.HttpMethod.Options,
            "TRACE" => System.Net.Http.HttpMethod.Trace,
            _ => System.Net.Http.HttpMethod.Get
        };
    }

    /// <summary>
    /// Determines whether the specified HTTP method typically has a request body.
    /// POST, PUT, and PATCH methods typically include body content.
    /// </summary>
    /// <param name="method">The HTTP method to check.</param>
    /// <returns>True if the method typically has a body; false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldHaveRequestBody(System.Net.Http.HttpMethod method)
    {
        return method == System.Net.Http.HttpMethod.Post ||
               method == System.Net.Http.HttpMethod.Put ||
               method == System.Net.Http.HttpMethod.Patch;
    }

    /// <summary>
    /// Determines whether the content type indicates JSON content.
    /// Checks for application/json and related JSON media types.
    /// </summary>
    /// <param name="contentType">The content type string to check.</param>
    /// <returns>True if the content type is JSON; false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsJsonContentType(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType) &&
               (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("text/json", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether an HTTP status code indicates a transient error.
    /// Transient errors are typically retryable (e.g., 5xx server errors, 408 timeout, 429 rate limit).
    /// </summary>
    /// <param name="statusCode">The HTTP status code to evaluate.</param>
    /// <returns>True if the status code represents a transient error; false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTransientStatusCode(System.Net.HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code >= 500 || code == 408 || code == 429;
    }
}
