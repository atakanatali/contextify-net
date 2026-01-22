using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Abstractions.Models;
using Contextify.Core.Catalog;
using Contextify.Core.Collections;
using Microsoft.Extensions.Logging;

namespace Contextify.Actions.Defaults.Actions;

/// <summary>
/// Action that enforces concurrency limits on tool invocations.
/// Applies when the effective policy specifies a concurrency limit greater than zero.
/// Uses per-tool semaphores to limit the number of concurrent executions.
/// Ensures fair resource allocation and prevents resource exhaustion.
/// </summary>
/// <remarks>
/// The semaphore cache is thread-safe and uses the tool name as the key.
/// Semaphores are created on-demand with bounded cache size to prevent unbounded growth.
/// Least recently used semaphores are evicted when the cache reaches its maximum size.
/// </remarks>
public sealed partial class ConcurrencyAction : IContextifyAction
{
    /// <summary>
    /// Gets the execution order for this action.
    /// Concurrency control should be applied after timeout (Order 110) to ensure
    /// that waiting for the semaphore doesn't count against the timeout.
    /// </summary>
    public int Order => 110;

    /// <summary>
    /// Maximum number of semaphores to cache per action instance.
    /// This bounds memory usage while supporting realistic numbers of tools.
    /// </summary>
    private const int MaxCachedSemaphores = 1024;

    /// <summary>
    /// Thread-safe bounded cache of semaphores indexed by tool name.
    /// Each semaphore controls concurrent access to a specific tool.
    /// The cache is bounded to prevent unbounded memory growth.
    /// </summary>
    private static readonly BoundedConcurrentCache<string, SemaphoreSlim> Semaphores = new(MaxCachedSemaphores, StringComparer.Ordinal);

    /// <summary>
    /// Gets the logger instance for diagnostics and tracing.
    /// </summary>
    private readonly ILogger<ConcurrencyAction> _logger;

    /// <summary>
    /// Initializes a new instance with the specified logger.
    /// </summary>
    /// <param name="logger">The logger instance for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public ConcurrencyAction(ILogger<ConcurrencyAction> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Determines whether this action should apply to the current invocation.
    /// Applies when the tool has an effective policy with a concurrency limit configured.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name and metadata.</param>
    /// <returns>
    /// True if the tool has a concurrency limit policy configured; false to skip this action.
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

        return toolDescriptor?.EffectivePolicy?.ConcurrencyLimit is not null and > 0;
    }

    /// <summary>
    /// Executes the concurrency enforcement logic asynchronously.
    /// Acquires a semaphore slot for the tool before proceeding, blocking if the limit is reached.
    /// Releases the semaphore slot after the pipeline completes.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name, arguments, and metadata.</param>
    /// <param name="next">
    /// The delegate representing the remaining actions in the pipeline.
    /// Call next() to continue processing after acquiring a semaphore slot.
    /// </param>
    /// <returns>
    /// A ValueTask representing the asynchronous operation, yielding the tool invocation result.
    /// </returns>
    public async ValueTask<ContextifyToolResultDto> InvokeAsync(
        ContextifyInvocationContextDto ctx,
        Func<ValueTask<ContextifyToolResultDto>> next)
    {
        var catalogProvider = ctx.GetRequiredService<ContextifyCatalogProviderService>();
        var snapshot = catalogProvider.GetSnapshot();

        if (!snapshot.TryGetTool(ctx.ToolName, out var toolDescriptor))
        {
            // Tool not found in catalog, proceed without concurrency control
            LogToolNotFound(ctx.ToolName);
            return await next();
        }

        var concurrencyLimit = toolDescriptor!.EffectivePolicy?.ConcurrencyLimit;
        if (concurrencyLimit is null or <= 0)
        {
            // No concurrency limit configured, proceed normally
            return await next();
        }

        // Get or create the semaphore for this tool
        var semaphore = Semaphores.GetOrAdd(
            ctx.ToolName,
            _ => new SemaphoreSlim(concurrencyLimit.Value, concurrencyLimit.Value));

        LogAcquiringSemaphore(ctx.ToolName, concurrencyLimit.Value, semaphore.CurrentCount);

        // Acquire the semaphore, respecting the original cancellation token
        var acquired = await semaphore.WaitAsync(
            timeout: TimeSpan.FromMinutes(5), // Safety timeout to prevent deadlocks
            cancellationToken: ctx.CancellationToken).ConfigureAwait(false);

        if (!acquired)
        {
            LogSemaphoreTimeout(ctx.ToolName);
            return new ContextifyToolResultDto(
                ContextifyToolErrorDto.InternalError(
                    $"Failed to acquire concurrency slot for tool '{ctx.ToolName}' within timeout."));
        }

        LogSemaphoreAcquired(ctx.ToolName, concurrencyLimit.Value, semaphore.CurrentCount);

        try
        {
            // Execute the remaining pipeline while holding the semaphore slot
            var result = await next().ConfigureAwait(false);
            return result;
        }
        finally
        {
            // Always release the semaphore slot
            semaphore.Release();
            LogSemaphoreReleased(ctx.ToolName, semaphore.CurrentCount);
        }
    }

    /// <summary>
    /// Logs a warning when a tool is not found in the catalog.
    /// </summary>
    [LoggerMessage(LogLevel.Warning, "Tool '{ToolName}' not found in catalog. ConcurrencyAction will not be applied.")]
    private partial void LogToolNotFound(string toolName);

    /// <summary>
    /// Logs information when attempting to acquire a semaphore slot.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Acquiring concurrency semaphore for tool '{ToolName}' (limit: {ConcurrencyLimit}, available: {AvailableCount}).")]
    private partial void LogAcquiringSemaphore(string toolName, int concurrencyLimit, int availableCount);

    /// <summary>
    /// Logs information when a semaphore slot is acquired.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Concurrency semaphore acquired for tool '{ToolName}' (limit: {ConcurrencyLimit}, available: {AvailableCount}).")]
    private partial void LogSemaphoreAcquired(string toolName, int concurrencyLimit, int availableCount);

    /// <summary>
    /// Logs a warning when semaphore acquisition times out.
    /// </summary>
    [LoggerMessage(LogLevel.Warning, "Failed to acquire concurrency semaphore for tool '{ToolName}' within timeout.")]
    private partial void LogSemaphoreTimeout(string toolName);

    /// <summary>
    /// Logs information when a semaphore slot is released.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Concurrency semaphore released for tool '{ToolName}' (available: {AvailableCount}).")]
    private partial void LogSemaphoreReleased(string toolName, int availableCount);
}
