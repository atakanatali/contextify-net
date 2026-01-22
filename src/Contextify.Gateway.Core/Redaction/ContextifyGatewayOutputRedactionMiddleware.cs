using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Contextify.Core.Redaction;
using Contextify.Gateway.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contextify.Gateway.Core.Redaction;

/// <summary>
/// ASP.NET Core middleware for redacting sensitive information from upstream tool responses.
/// Applies field-name based JSON redaction and optional pattern-based text redaction.
/// Designed for high-performance scenarios with minimal allocation when redaction is disabled.
/// Intercepts MCP tool call responses before they are sent to the MCP client.
/// </summary>
public sealed partial class ContextifyGatewayOutputRedactionMiddleware
{
    /// <summary>
    /// The JSON-RPC tools/call method name that indicates a tool invocation response.
    /// </summary>
    private const string ToolsCallMethod = "tools/call";

    /// <summary>
    /// The HTTP response body key used to store the original response stream for restoration.
    /// </summary>
    private const string OriginalResponseBodyKey = "OriginalResponseBody";

    /// <summary>
    /// Gets the next middleware in the ASP.NET Core pipeline.
    /// </summary>
    private readonly RequestDelegate _next;

    /// <summary>
    /// Gets the logger instance for diagnostics and tracing.
    /// </summary>
    private readonly ILogger<ContextifyGatewayOutputRedactionMiddleware> _logger;

    /// <summary>
    /// Gets the redaction service for performing sensitive information redaction.
    /// </summary>
    private readonly IContextifyRedactionService _redactionService;

    /// <summary>
    /// Gets the redaction options entity to check if redaction is enabled.
    /// </summary>
    private readonly ContextifyRedactionOptionsEntity _options;

    /// <summary>
    /// Initializes a new instance with the specified dependencies.
    /// </summary>
    /// <param name="next">The next middleware in the ASP.NET Core pipeline.</param>
    /// <param name="logger">The logger instance for diagnostics.</param>
    /// <param name="redactionService">The redaction service for sanitizing output.</param>
    /// <param name="options">The redaction configuration options.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyGatewayOutputRedactionMiddleware(
        RequestDelegate next,
        ILogger<ContextifyGatewayOutputRedactionMiddleware> logger,
        IContextifyRedactionService redactionService,
        IOptions<ContextifyRedactionOptionsEntity> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _redactionService = redactionService ?? throw new ArgumentNullException(nameof(redactionService));

        var optionsValue = options?.Value
            ?? throw new ArgumentNullException(nameof(options));
        _options = optionsValue;
    }

    /// <summary>
    /// Processes an HTTP request to apply output redaction for MCP responses.
    /// Only processes responses to MCP endpoints with JSON content.
    /// Redacts sensitive information from tool call result content.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Skip redaction if disabled
        if (!_options.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Store original response body stream
        var originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        try
        {
            // Call next middleware in the pipeline
            await _next(context).ConfigureAwait(false);

            // Only redact JSON responses
            if (!ShouldRedactResponse(context))
            {
                // Not a JSON response, skip redaction
                await CopyResponseBodyAsync(context, originalBodyStream).ConfigureAwait(false);
                return;
            }

            // Read and potentially redact the response
            memoryStream.Position = 0;
            var responseText = await new StreamReader(memoryStream).ReadToEndAsync().ConfigureAwait(false);

            // Try to redact the JSON response
            var redactedResponse = TryRedactJsonResponse(responseText, out var wasRedacted);

            if (wasRedacted)
            {
                LogResponseRedacted(context.Request.Path);
            }

            // Write the (potentially redacted) response
            context.Response.Body = originalBodyStream;
            context.Response.ContentLength = null; // Reset to allow recalculation
            await context.Response.WriteAsync(redactedResponse, context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            // Always restore the original response body
            context.Response.Body = originalBodyStream;
        }
    }

    /// <summary>
    /// Determines whether the response should be redacted based on content type.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>True if the response is JSON and should be redacted; false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldRedactResponse(HttpContext context)
    {
        // Only redact JSON responses
        return context.Response.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Attempts to redact sensitive information from a JSON-RPC response.
    /// Handles JSON parsing errors gracefully by returning the original response.
    /// </summary>
    /// <param name="responseText">The JSON response text to redact.</param>
    /// <param name="wasRedacted">Set to true if redaction was applied.</param>
    /// <returns>The redacted JSON response text, or the original if redaction failed.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Parsing errors are handled gracefully by returning the original response unmodified.")]
    private string TryRedactJsonResponse(string responseText, out bool wasRedacted)
    {
        wasRedacted = false;

        // Fast path: no fields or patterns configured
        if (_options.RedactJsonFields.Count == 0 && _options.RedactPatterns.Count == 0)
        {
            return responseText;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            // Check if this is a JSON-RPC response with a result
            if (root.TryGetProperty("result", out var resultProperty))
            {
                // Check if the result has content to redact
                if (resultProperty.TryGetProperty("content", out var contentProperty))
                {
                    // Redact the content
                    var redactedContent = _redactionService.RedactJson(contentProperty);

                    if (redactedContent.HasValue && !redactedContent.Value.Equals(contentProperty))
                    {
                        // Content was redacted, rebuild the response
                        wasRedacted = true;
                        return RebuildResponseWithRedactedContent(root, redactedContent.Value);
                    }
                }
            }

            return responseText;
        }
        catch
        {
            // If JSON parsing fails, return the original response unchanged
            // This ensures the gateway remains functional even with malformed JSON
            return responseText;
        }
    }

    /// <summary>
    /// Rebuilds a JSON-RPC response with redacted content.
    /// Creates a new JSON object with the redacted content replacing the original.
    /// </summary>
    /// <param name="root">The original JSON-RPC response root element.</param>
    /// <param name="redactedContent">The redacted content element.</param>
    /// <returns>A JSON string representing the redacted response.</returns>
    [SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly",
        Justification = "Exception messages reference the parameter name for clarity.")]
    private static string RebuildResponseWithRedactedContent(JsonElement root, JsonElement redactedContent)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WritePropertyName("jsonrpc");
        writer.WriteStringValue(root.GetProperty("jsonrpc").GetString() ?? "2.0");

        if (root.TryGetProperty("id", out var idProperty))
        {
            writer.WritePropertyName("id");
            WriteJsonElement(writer, idProperty);
        }

        writer.WritePropertyName("result");
        writer.WriteStartObject();

        // Copy all result properties except content
        var result = root.GetProperty("result");
        foreach (var property in result.EnumerateObject())
        {
            if (property.NameEquals("content"))
            {
                // Write the redacted content
                writer.WritePropertyName("content");
                WriteJsonElement(writer, redactedContent);
            }
            else
            {
                // Copy other properties as-is
                writer.WritePropertyName(property.Name);
                WriteJsonElement(writer, property.Value);
            }
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Writes a JsonElement to a Utf8JsonWriter for JSON reconstruction.
    /// Handles all JSON value kinds including objects, arrays, and primitives.
    /// </summary>
    /// <param name="writer">The JSON writer to write to.</param>
    /// <param name="element">The JSON element to write.</param>
    private static void WriteJsonElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString() ?? string.Empty);
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longValue))
                {
                    writer.WriteNumberValue(longValue);
                }
                else if (element.TryGetDouble(out var doubleValue))
                {
                    writer.WriteNumberValue(doubleValue);
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

            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteJsonElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteJsonElement(writer, item);
                }
                writer.WriteEndArray();
                break;

            default:
                writer.WriteNullValue();
                break;
        }
    }

    /// <summary>
    /// Copies the response body from the memory stream back to the original stream.
    /// Used when redaction is not applied to restore the original response.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="originalBodyStream">The original response body stream.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task CopyResponseBodyAsync(HttpContext context, Stream originalBodyStream)
    {
        context.Response.Body = originalBodyStream;
        context.Response.Body.Position = 0;
        await context.Response.Body.CopyToAsync(originalBodyStream, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs information when a response is redacted.
    /// </summary>
    /// <param name="path">The request path.</param>
    [LoggerMessage(LogLevel.Information, "Response redacted for path '{Path}'.")]
    private partial void LogResponseRedacted(string path);
}

/// <summary>
/// Extension methods for registering the output redaction middleware in the ASP.NET Core pipeline.
/// </summary>
public static class ContextifyGatewayOutputRedactionMiddlewareExtensions
{
    /// <summary>
    /// Adds the Contextify Gateway output redaction middleware to the application pipeline.
    /// Should be registered after endpoint routing but before response execution.
    /// </summary>
    /// <param name="builder">The application builder to add middleware to.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    /// <remarks>
    /// This middleware redacts sensitive information from upstream tool responses.
    /// Configure redaction via IContextifyRedactionService options to enable and specify fields/patterns.
    /// For optimal performance, register this middleware early in the pipeline.
    /// </remarks>
    public static IApplicationBuilder UseContextifyGatewayOutputRedaction(
        this IApplicationBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.UseMiddleware<ContextifyGatewayOutputRedactionMiddleware>();
    }
}
