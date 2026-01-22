using System;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Abstractions.Models;
using Contextify.Core.Redaction;
using Microsoft.Extensions.Logging;

namespace Contextify.Actions.Defaults.Actions;

/// <summary>
/// Action that redacts sensitive information from tool output payloads.
/// Applies after tool execution (Order 200) to sanitize responses before returning to MCP clients.
/// Uses field-name based JSON redaction and optional pattern-based text redaction.
/// Designed for high-performance scenarios with minimal allocation when redaction is disabled.
/// </summary>
public sealed partial class OutputRedactionAction : IContextifyAction
{
    /// <summary>
    /// Gets the execution order for this action.
    /// Output redaction should be applied after tool execution but before response is returned.
    /// Order 200 places this after core actions (timeout, concurrency, rate limit) but before final output processing.
    /// </summary>
    public int Order => 200;

    /// <summary>
    /// Gets the redaction service for performing sensitive information redaction.
    /// </summary>
    private readonly IContextifyRedactionService _redactionService;

    /// <summary>
    /// Gets the logger instance for diagnostics and tracing.
    /// </summary>
    private readonly ILogger<OutputRedactionAction> _logger;

    /// <summary>
    /// Initializes a new instance with the specified redaction service and logger.
    /// </summary>
    /// <param name="redactionService">The redaction service for sanitizing output.</param>
    /// <param name="logger">The logger instance for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when redactionService or logger is null.</exception>
    public OutputRedactionAction(
        IContextifyRedactionService redactionService,
        ILogger<OutputRedactionAction> logger)
    {
        _redactionService = redactionService ?? throw new ArgumentNullException(nameof(redactionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Determines whether this action should apply to the current invocation.
    /// Always returns true to allow redaction service to determine whether redaction should occur.
    /// The redaction service itself checks if redaction is enabled, avoiding unnecessary overhead.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name, arguments, and metadata.</param>
    /// <returns>
    /// True to allow redaction service to determine applicability; always true in this implementation.
    /// </returns>
    /// <remarks>
    /// This method does not check redaction configuration to avoid service lookups on every invocation.
    /// The redaction service itself provides fast-path returns when redaction is disabled.
    /// </remarks>
    public bool AppliesTo(in ContextifyInvocationContextDto ctx)
    {
        // Always return true; let the redaction service handle the fast-path check
        // This avoids service lookup overhead when redaction is disabled
        return true;
    }

    /// <summary>
    /// Executes the output redaction logic asynchronously.
    /// Invokes the next action in the pipeline, then redacts sensitive information from the result.
    /// Redaction applies to both TextContent and JsonContent properties of the result.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name, arguments, and metadata.</param>
    /// <param name="next">
    /// The delegate representing the remaining actions in the pipeline.
    /// Call next() to continue processing with output redaction applied to the result.
    /// </param>
    /// <returns>
    /// A ValueTask representing the asynchronous operation, yielding the tool invocation result with redacted output.
    /// </returns>
    /// <remarks>
    /// Redaction is applied after the tool executes to sanitize all output formats.
    /// JSON content is redacted using field-name matching for optimal performance.
    /// Text content is redacted using configured patterns (if any).
    /// Error messages are not redacted to preserve debugging information.
    /// </remarks>
    public async ValueTask<ContextifyToolResultDto> InvokeAsync(
        ContextifyInvocationContextDto ctx,
        Func<ValueTask<ContextifyToolResultDto>> next)
    {
        // Invoke the next action in the pipeline
        var result = await next().ConfigureAwait(false);

        // Do not redact error results
        if (!result.IsSuccess)
        {
            return result;
        }

        // Redact text content if present
        if (result.TextContent is not null)
        {
            var redactedText = _redactionService.RedactText(result.TextContent);
            if (redactedText != result.TextContent)
            {
                LogRedactedTextContent(ctx.ToolName);
                result = result with { TextContent = redactedText };
            }
        }

        // Redact JSON content if present
        if (result.JsonContent is not null)
        {
            // Convert JsonNode to JsonElement for redaction
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(result.JsonContent);
            var redactedJson = _redactionService.RedactJson(jsonElement);

            if (redactedJson.HasValue && redactedJson.Value.ValueKind != JsonValueKind.Undefined && !redactedJson.Value.Equals(jsonElement))
            {
                LogRedactedJsonContent(ctx.ToolName);
                // Convert back to JsonNode for the result
                var rawText = redactedJson.Value.GetRawText();
                result = result with { JsonContent = JsonNode.Parse(rawText) };
            }
        }

        return result;
    }

    /// <summary>
    /// Logs information when text content is redacted.
    /// </summary>
    /// <param name="toolName">The name of the tool whose output was redacted.</param>
    [LoggerMessage(LogLevel.Information, "Text content redacted for tool '{ToolName}'.")]
    private partial void LogRedactedTextContent(string toolName);

    /// <summary>
    /// Logs information when JSON content is redacted.
    /// </summary>
    /// <param name="toolName">The name of the tool whose output was redacted.</param>
    [LoggerMessage(LogLevel.Information, "JSON content redacted for tool '{ToolName}'.")]
    private partial void LogRedactedJsonContent(string toolName);
}
