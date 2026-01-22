using System;
using System.Threading.Tasks;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Abstractions.Models;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using Contextify.Core.Execution;
using Microsoft.Extensions.Logging;

namespace Contextify.Actions.Defaults.Actions;

/// <summary>
/// Action that validates authentication context propagation for tool execution.
/// Applies when the effective policy specifies an auth propagation mode other than None.
/// Validates that auth context from the invocation matches the propagation requirements.
/// The actual propagation to downstream HTTP requests is handled by the tool executor.
/// </summary>
public sealed partial class AuthPropagationAction : IContextifyAction
{
    /// <summary>
    /// Gets the execution order for this action.
    /// Auth propagation should run late in the pipeline (Order 90) to ensure the context is populated
    /// before tool execution but after other preprocessing actions.
    /// </summary>
    public int Order => 90;

    /// <summary>
    /// Gets the logger instance for diagnostics and tracing.
    /// </summary>
    private readonly ILogger<AuthPropagationAction> _logger;

    /// <summary>
    /// Initializes a new instance with the specified logger.
    /// </summary>
    /// <param name="logger">The logger instance for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public AuthPropagationAction(ILogger<AuthPropagationAction> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Determines whether this action should apply to the current invocation.
    /// Applies when the tool has an effective policy with auth propagation mode not equal to None.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name and metadata.</param>
    /// <returns>
    /// True if the tool has an auth propagation policy configured other than None; false to skip.
    /// </returns>
    public bool AppliesTo(in ContextifyInvocationContextDto ctx)
    {
        var catalogProvider = ctx.GetService<ContextifyCatalogProviderService>();
        if (catalogProvider is null)
        {
            return false;
        }

        var snapshot = catalogProvider.GetSnapshot();
        if (!snapshot.TryGetTool(ctx.ToolName, out var toolDescriptor))
        {
            return false;
        }

        return toolDescriptor?.EffectivePolicy?.AuthPropagationMode != ContextifyAuthPropagationMode.None;
    }

    /// <summary>
    /// Executes the auth propagation validation logic asynchronously.
    /// Validates that the auth context in the invocation matches the propagation mode requirements.
    /// The actual propagation to downstream requests is handled by ContextifyToolExecutorService.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name, arguments, and metadata.</param>
    /// <param name="next">
    /// The delegate representing the remaining actions in the pipeline.
    /// Call next() to continue processing with auth propagation validated.
    /// </param>
    /// <returns>
    /// A ValueTask representing the asynchronous operation, yielding the tool invocation result.
    /// </returns>
    public ValueTask<ContextifyToolResultDto> InvokeAsync(
        ContextifyInvocationContextDto ctx,
        Func<ValueTask<ContextifyToolResultDto>> next)
    {
        var catalogProvider = ctx.GetRequiredService<ContextifyCatalogProviderService>();
        var snapshot = catalogProvider.GetSnapshot();

        if (!snapshot.TryGetTool(ctx.ToolName, out var toolDescriptor))
        {
            // Tool not found in catalog, proceed without auth propagation
            LogToolNotFound(ctx.ToolName);
            return next();
        }

        var propagationMode = toolDescriptor!.EffectivePolicy?.AuthPropagationMode ?? ContextifyAuthPropagationMode.None;
        if (propagationMode == ContextifyAuthPropagationMode.None)
        {
            // No propagation needed, proceed normally
            return next();
        }

        // Validate auth context based on propagation mode
        ValidateAuthContext(ctx.ToolName, ctx.AuthContext, propagationMode);

        return next();
    }

    /// <summary>
    /// Validates that the auth context is appropriate for the propagation mode.
    /// Logs diagnostic information about auth context availability and mode compatibility.
    /// </summary>
    /// <param name="toolName">The name of the tool being invoked.</param>
    /// <param name="authContext">The auth context from the invocation (may be null).</param>
    /// <param name="propagationMode">The required propagation mode from policy.</param>
    private void ValidateAuthContext(
        string toolName,
        ContextifyAuthContextDto? authContext,
        ContextifyAuthPropagationMode propagationMode)
    {
        if (authContext is null)
        {
            LogAuthContextMissing(toolName, propagationMode);
            return;
        }

        // Log what auth context is available
        if (!string.IsNullOrWhiteSpace(authContext.BearerToken))
        {
            LogBearerTokenPresent(toolName, propagationMode);
        }

        if (!string.IsNullOrWhiteSpace(authContext.ApiKey))
        {
            LogApiKeyPresent(toolName, authContext.ApiKeyHeaderName ?? "X-API-Key", propagationMode);
        }

        if (authContext.AdditionalHeaders.Count > 0)
        {
            LogAdditionalHeadersPresent(toolName, authContext.AdditionalHeaders.Count, propagationMode);
        }

        LogAuthContextValidated(toolName, propagationMode);
    }

    /// <summary>
    /// Logs a warning when a tool is not found in the catalog.
    /// </summary>
    [LoggerMessage(LogLevel.Warning, "Tool '{ToolName}' not found in catalog. AuthPropagationAction will not be applied.")]
    private partial void LogToolNotFound(string toolName);

    /// <summary>
    /// Logs information when auth context is missing but propagation mode requires it.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Auth context missing for tool '{ToolName}'. Propagation mode: {PropagationMode}. Downstream requests will be anonymous.")]
    private partial void LogAuthContextMissing(string toolName, ContextifyAuthPropagationMode propagationMode);

    /// <summary>
    /// Logs information when bearer token is present in auth context.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Bearer token present for tool '{ToolName}'. Propagation mode: {PropagationMode}.")]
    private partial void LogBearerTokenPresent(string toolName, ContextifyAuthPropagationMode propagationMode);

    /// <summary>
    /// Logs information when API key is present in auth context.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "API key present for tool '{ToolName}'. Header: {HeaderName}. Propagation mode: {PropagationMode}.")]
    private partial void LogApiKeyPresent(string toolName, string headerName, ContextifyAuthPropagationMode propagationMode);

    /// <summary>
    /// Logs information when additional headers are present in auth context.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Additional headers present for tool '{ToolName}'. Count: {HeaderCount}. Propagation mode: {PropagationMode}.")]
    private partial void LogAdditionalHeadersPresent(string toolName, int headerCount, ContextifyAuthPropagationMode propagationMode);

    /// <summary>
    /// Logs information when auth context validation is complete.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Auth context validated for tool '{ToolName}'. Propagation mode: {PropagationMode}.")]
    private partial void LogAuthContextValidated(string toolName, ContextifyAuthPropagationMode propagationMode);
}
