using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.RateLimiting;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Abstractions.Models;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using Contextify.Core.Collections;
using Microsoft.Extensions.Logging;

namespace Contextify.Actions.Defaults.Actions;

/// <summary>
/// Action that enforces rate limiting on tool invocations.
/// Applies when the effective policy specifies a rate limit policy.
/// Uses System.Threading.RateLimiter to control request rates based on the policy configuration.
/// Supports tenant-aware rate limiting when tenant information is available in the context.
/// </summary>
/// <remarks>
/// Rate limiter instances are created per unique key (tool name or tool name + tenant).
/// Rate limiters are cached with bounded size to prevent unbounded memory growth.
/// The rate limiting algorithm is determined by the policy strategy (e.g., FixedWindow, TokenBucket).
/// When rate limit is exceeded, returns a structured error response instead of throwing.
/// </remarks>
public sealed partial class RateLimitAction : IContextifyAction
{
    /// <summary>
    /// Gets the execution order for this action.
    /// Rate limiting should be applied after concurrency control (Order 120).
    /// </summary>
    public int Order => 120;

    /// <summary>
    /// The key used to store tenant information in the context extensions.
    /// Allows for tenant-aware rate limiting when present.
    /// </summary>
    private const string TenantIdKey = "tenantId";

    /// <summary>
    /// Maximum number of rate limiters to cache per action instance.
    /// This bounds memory usage while supporting realistic numbers of unique rate limit keys.
    /// Consider: tools * tenants in multi-tenant scenarios.
    /// </summary>
    private const int MaxCachedRateLimiters = 2048;

    /// <summary>
    /// Thread-safe bounded cache of rate limiters indexed by their unique key.
    /// Each rate limiter controls request rates for a specific key combination.
    /// The cache is bounded to prevent unbounded memory growth in high-tenant scenarios.
    /// </summary>
    private readonly BoundedConcurrentCache<string, RateLimiter> _rateLimiters;

    /// <summary>
    /// Gets the logger instance for diagnostics and tracing.
    /// </summary>
    private readonly ILogger<RateLimitAction> _logger;

    /// <summary>
    /// Initializes a new instance with the specified logger.
    /// </summary>
    /// <param name="logger">The logger instance for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public RateLimitAction(ILogger<RateLimitAction> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimiters = new BoundedConcurrentCache<string, RateLimiter>(MaxCachedRateLimiters, StringComparer.Ordinal);
    }

    /// <summary>
    /// Determines whether this action should apply to the current invocation.
    /// Applies when the tool has an effective policy with a rate limit policy configured.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name and metadata.</param>
    /// <returns>
    /// True if the tool has a rate limit policy configured; false to skip this action.
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

        return toolDescriptor?.EffectivePolicy?.RateLimitPolicy is not null;
    }

    /// <summary>
    /// Executes the rate limiting enforcement logic asynchronously.
    /// Attempts to acquire a permit from the appropriate rate limiter.
    /// If the permit is acquired, proceeds with the pipeline; otherwise returns a rate limit error.
    /// </summary>
    /// <param name="ctx">The invocation context containing tool name, arguments, and metadata.</param>
    /// <param name="next">
    /// The delegate representing the remaining actions in the pipeline.
    /// Call next() to continue processing after acquiring a rate limit permit.
    /// </param>
    /// <returns>
    /// A ValueTask representing the asynchronous operation, yielding the tool invocation result.
    /// Returns a rate limit error if the request would exceed the configured rate limit.
    /// </returns>
    public async ValueTask<ContextifyToolResultDto> InvokeAsync(
        ContextifyInvocationContextDto ctx,
        Func<ValueTask<ContextifyToolResultDto>> next)
    {
        var catalogProvider = ctx.GetRequiredService<ContextifyCatalogProviderService>();
        var snapshot = catalogProvider.GetSnapshot();

        if (!snapshot.TryGetTool(ctx.ToolName, out var toolDescriptor))
        {
            // Tool not found in catalog, proceed without rate limiting
            LogToolNotFound(ctx.ToolName);
            return await next();
        }

        var rateLimitPolicy = toolDescriptor!.EffectivePolicy?.RateLimitPolicy;
        if (rateLimitPolicy is null)
        {
            // No rate limit policy configured, proceed normally
            return await next();
        }

        // Build the rate limiter key based on policy configuration
        var rateLimiterKey = BuildRateLimiterKey(ctx.ToolName, rateLimitPolicy, ctx);

        // Get or create the rate limiter for this key
        var rateLimiter = _rateLimiters.GetOrAdd(
            rateLimiterKey,
            _ => CreateRateLimiter(rateLimitPolicy));

        LogCheckingRateLimit(ctx.ToolName, rateLimiterKey);

        // Attempt to acquire a permit from the rate limiter
        using var permit = await rateLimiter.AcquireAsync(
            permitCount: 1,
            cancellationToken: ctx.CancellationToken).ConfigureAwait(false);

        if (!permit.IsAcquired)
        {
            // Rate limit exceeded - return a structured error
            // Note: RetryAfter metadata may be available depending on the rate limiter type
            var retryAfterSeconds = (int?)null; // Default to null as we can't reliably extract retry time

            LogRateLimitExceeded(ctx.ToolName, rateLimiterKey, retryAfterSeconds);

            return new ContextifyToolResultDto(
                ContextifyToolErrorDto.RateLimitError(
                    $"Rate limit exceeded for tool '{ctx.ToolName}'. Please retry later.",
                    retryAfterSeconds: retryAfterSeconds));
        }

        LogRateLimitPermitAcquired(ctx.ToolName, rateLimiterKey);

        // Permit acquired, proceed with the pipeline
        return await next().ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the rate limiter key based on the policy configuration and context.
    /// Uses tool name as the base key, optionally appending tenant information.
    /// </summary>
    /// <param name="toolName">The name of the tool being invoked.</param>
    /// <param name="rateLimitPolicy">The rate limit policy configuration.</param>
    /// <param name="ctx">The invocation context containing tenant information if available.</param>
    /// <returns>A unique key for rate limiting this request.</returns>
    private string BuildRateLimiterKey(
        string toolName,
        ContextifyRateLimitPolicyDto rateLimitPolicy,
        in ContextifyInvocationContextDto ctx)
    {
        // Check if we should use tenant-aware rate limiting
        // This is determined by either the policy scope or segmentation key
        var useTenantKey = ShouldUseTenantKey(rateLimitPolicy);

        if (useTenantKey && TryGetTenantId(ctx, out var tenantId))
        {
            return $"tool:{toolName}:tenant:{tenantId}";
        }

        return $"tool:{toolName}";
    }

    /// <summary>
    /// Determines whether to use tenant-aware rate limiting based on policy configuration.
    /// </summary>
    /// <param name="rateLimitPolicy">The rate limit policy configuration.</param>
    /// <returns>True if tenant-aware rate limiting should be used; false otherwise.</returns>
    private bool ShouldUseTenantKey(ContextifyRateLimitPolicyDto rateLimitPolicy)
    {
        // Use tenant key if policy scope contains "tenant" or segmentation key is set
        var scope = rateLimitPolicy.Scope ?? string.Empty;
        return scope.Contains("tenant", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(rateLimitPolicy.SegmentationKey);
    }

    /// <summary>
    /// Attempts to extract the tenant ID from the invocation context.
    /// Checks the service provider for tenant context or looks for tenant in arguments.
    /// </summary>
    /// <param name="ctx">The invocation context.</param>
    /// <param name="tenantId">The extracted tenant ID, if found.</param>
    /// <returns>True if tenant ID was found; false otherwise.</returns>
    private bool TryGetTenantId(in ContextifyInvocationContextDto ctx, out string? tenantId)
    {
        tenantId = null;

        // Try to get tenant from a tenant context service if available
        // This assumes there may be a ITenantContext service registered
        var tenantContext = ctx.GetService<ITenantContext>();
        if (tenantContext is not null)
        {
            tenantId = tenantContext.TenantId;
            return !string.IsNullOrWhiteSpace(tenantId);
        }

        // Try to get tenant from arguments (fallback mechanism)
        if (ctx.Arguments.TryGetValue(TenantIdKey, out var tenantValue) &&
            tenantValue is string tenantString &&
            !string.IsNullOrWhiteSpace(tenantString))
        {
            tenantId = tenantString;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates a rate limiter instance based on the policy configuration.
    /// Supports multiple rate limiting strategies including FixedWindow, SlidingWindow, and TokenBucket.
    /// </summary>
    /// <param name="rateLimitPolicy">The rate limit policy configuration.</param>
    /// <returns>A configured RateLimiter instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when policy configuration is invalid or strategy is unsupported.</exception>
    private RateLimiter CreateRateLimiter(ContextifyRateLimitPolicyDto rateLimitPolicy)
    {
        var permitLimit = rateLimitPolicy.PermitLimit ?? throw new InvalidOperationException(
            $"Rate limit policy must specify {nameof(rateLimitPolicy.PermitLimit)}.");

        var strategy = (rateLimitPolicy.Strategy ?? "FixedWindow").ToUpperInvariant();

        RateLimiter rateLimiter;
        double windowMsForLogging;

        switch (strategy)
        {
            case "TOKENBUCKET":
                var refillPeriod = rateLimitPolicy.RefillPeriodMs ?? throw new InvalidOperationException(
                    $"TokenBucket strategy requires {nameof(rateLimitPolicy.RefillPeriodMs)}.");
                rateLimiter = new TokenBucketRateLimiter(
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = permitLimit,
                        QueueLimit = rateLimitPolicy.QueueLimit ?? 0,
                        ReplenishmentPeriod = TimeSpan.FromMilliseconds(refillPeriod),
                        TokensPerPeriod = rateLimitPolicy.TokensPerPeriod ?? 1,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
                windowMsForLogging = refillPeriod;
                break;

            case "SLIDINGWINDOW":
                var window = TimeSpan.FromMilliseconds(
                    rateLimitPolicy.WindowMs ?? throw new InvalidOperationException(
                        "SlidingWindow strategy requires WindowMs."));
                rateLimiter = new SlidingWindowRateLimiter(
                    new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = window,
                        SegmentsPerWindow = 3,
                        QueueLimit = rateLimitPolicy.QueueLimit ?? 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
                windowMsForLogging = window.TotalMilliseconds;
                break;

            default: // FixedWindow
                var defaultWindow = TimeSpan.FromMilliseconds(
                    rateLimitPolicy.WindowMs ?? throw new InvalidOperationException(
                        "FixedWindow strategy requires WindowMs."));
                rateLimiter = new FixedWindowRateLimiter(
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = defaultWindow,
                        QueueLimit = rateLimitPolicy.QueueLimit ?? 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
                windowMsForLogging = defaultWindow.TotalMilliseconds;
                break;
        }

        LogRateLimiterCreated(strategy, permitLimit, windowMsForLogging);
        return rateLimiter;
    }

    /// <summary>
    /// Logs a warning when a tool is not found in the catalog.
    /// </summary>
    [LoggerMessage(LogLevel.Warning, "Tool '{ToolName}' not found in catalog. RateLimitAction will not be applied.")]
    private partial void LogToolNotFound(string toolName);

    /// <summary>
    /// Logs information when checking rate limit for a tool invocation.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Checking rate limit for tool '{ToolName}' with key '{RateLimiterKey}'.")]
    private partial void LogCheckingRateLimit(string toolName, string rateLimiterKey);

    /// <summary>
    /// Logs information when a rate limit permit is acquired.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Rate limit permit acquired for tool '{ToolName}' with key '{RateLimiterKey}'.")]
    private partial void LogRateLimitPermitAcquired(string toolName, string rateLimiterKey);

    /// <summary>
    /// Logs a warning when rate limit is exceeded.
    /// </summary>
    [LoggerMessage(LogLevel.Warning, "Rate limit exceeded for tool '{ToolName}' with key '{RateLimiterKey}'. Retry after: {RetryAfterSeconds}s")]
    private partial void LogRateLimitExceeded(string toolName, string rateLimiterKey, double? retryAfterSeconds);

    /// <summary>
    /// Logs information when a rate limiter is created.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Created {Strategy} rate limiter with permit limit {PermitLimit} and window {WindowMs}ms.")]
    private partial void LogRateLimiterCreated(string strategy, int permitLimit, double windowMs);
}

/// <summary>
/// Interface for accessing tenant context in rate limiting scenarios.
/// Implementations provide the current tenant ID for tenant-aware rate limiting.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Gets the current tenant identifier.
    /// Null or empty indicates no tenant context is available.
    /// </summary>
    string? TenantId { get; }
}
