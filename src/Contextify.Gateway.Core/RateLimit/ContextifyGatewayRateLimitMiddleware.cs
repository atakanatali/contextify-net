using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using Contextify.Gateway.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contextify.Gateway.Core.RateLimit;

/// <summary>
/// ASP.NET Core middleware for rate limiting MCP calls in the Contextify Gateway.
/// Applies policy-based rate limiting to tools/call requests with multi-tenant support.
/// Uses sliding window rate limiting for smooth quota enforcement across different scopes.
/// </summary>
public sealed class ContextifyGatewayRateLimitMiddleware
{
    private const string ToolsCallMethod = "tools/call";
    private const string RateLimitLeaseMetadataKey = "RateLimitLease";

    private readonly RequestDelegate _next;
    private readonly ILogger<ContextifyGatewayRateLimitMiddleware> _logger;
    private readonly ContextifyGatewayRateLimitOptionsEntity _options;
    private readonly ContextifyGatewayTenantResolutionOptionsEntity _tenantOptions;
    private readonly ContextifyGatewayRateLimiterCache _limiterCache;

    /// <summary>
    /// Initializes a new instance of the rate limit middleware.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for diagnostics and tracing.</param>
    /// <param name="options">Rate limiting configuration options.</param>
    /// <param name="tenantOptions">Tenant resolution configuration options.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyGatewayRateLimitMiddleware(
        RequestDelegate next,
        ILogger<ContextifyGatewayRateLimitMiddleware> logger,
        IOptions<ContextifyGatewayRateLimitOptionsEntity> options,
        IOptions<ContextifyGatewayTenantResolutionOptionsEntity> tenantOptions)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var optionsValue = options?.Value
            ?? throw new ArgumentNullException(nameof(options));
        var tenantOptionsValue = tenantOptions?.Value
            ?? throw new ArgumentNullException(nameof(tenantOptions));

        _options = optionsValue;
        _tenantOptions = tenantOptionsValue;
        _limiterCache = new ContextifyGatewayRateLimiterCache(
            _options.MaxCacheSize,
            _options.EntryExpiration);
    }

    /// <summary>
    /// Processes an HTTP request to apply rate limiting for MCP calls.
    /// Only processes requests to MCP endpoints, applying rate limiting to tools/call methods.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Skip rate limiting if disabled
        if (!_options.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Only rate limit MCP endpoints
        if (!IsMcpEndpoint(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Check if this is a tools/call request
        var (isToolsCall, toolName) = await TryGetToolCallDetailsAsync(context).ConfigureAwait(false);

        if (!isToolsCall)
        {
            // Not a tool call, skip rate limiting
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Get the applicable quota policy
        var policy = _options.GetPolicyForTool(toolName);

        if (policy == null)
        {
            // No policy configured, skip rate limiting
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Build the rate limit key based on scope
        var rateLimitKey = BuildRateLimitKey(context, policy.Scope, toolName);

        // Apply rate limiting
        var result = await TryAcquireLeaseAsync(rateLimitKey, policy, context.RequestAborted).ConfigureAwait(false);

        if (!result.IsAcquired)
        {
            // Rate limit exceeded
            await WriteRateLimitExceededResponseAsync(context, rateLimitKey, policy).ConfigureAwait(false);
            return;
        }

        // Store lease metadata for potential later use
        context.Items[RateLimitLeaseMetadataKey] = result;

        // Continue processing
        await _next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines whether the request path targets an MCP endpoint.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <returns>True if the path is an MCP endpoint; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsMcpEndpoint(string path)
    {
        // Check for common MCP endpoint patterns
        return path.Equals("/mcp", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/mcp/", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts tool call details from the HTTP request.
    /// Parses the JSON-RPC request body to determine if it's a tools/call method.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A tuple indicating whether this is a tool call and the tool name if so.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Parsing errors are handled gracefully by returning false for non-tool-call requests.")]
    private static async Task<(bool IsToolsCall, string ToolName)> TryGetToolCallDetailsAsync(HttpContext context)
    {
        // Only process POST requests
        if (!string.Equals(context.Request.Method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return (false, string.Empty);
        }

        // Enable request buffering for re-reading by downstream handlers
        context.Request.EnableBuffering();

        var originalPosition = context.Request.Body.Position;
        context.Request.Body.Position = 0;

        try
        {
            // Parse JSON to extract method and potentially tool name
            using var document = await System.Text.Json.JsonDocument.ParseAsync(
                context.Request.Body,
                cancellationToken: context.RequestAborted).ConfigureAwait(false);

            var root = document.RootElement;

            // Check if this is a tools/call method
            if (root.TryGetProperty("method", out var methodProperty) &&
                methodProperty.ValueKind == System.Text.Json.JsonValueKind.String &&
                methodProperty.GetString() == ToolsCallMethod)
            {
                // Try to extract tool name for policy matching
                var toolName = string.Empty;
                if (root.TryGetProperty("params", out var paramsProperty) &&
                    paramsProperty.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (paramsProperty.TryGetProperty("name", out var nameProperty) &&
                        nameProperty.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        toolName = nameProperty.GetString() ?? string.Empty;
                    }
                }

                return (true, toolName);
            }

            return (false, string.Empty);
        }
        catch
        {
            // If parsing fails, assume not a tool call and let downstream handlers deal with it
            return (false, string.Empty);
        }
        finally
        {
            // Reset position for downstream handlers
            context.Request.Body.Position = originalPosition;
        }
    }

    /// <summary>
    /// Builds a rate limit key based on the quota scope and request context.
    /// The key determines which requests share the same rate limiter.
    /// </summary>
    /// <param name="context">The HTTP context containing request metadata.</param>
    /// <param name="scope">The quota scope determining key composition.</param>
    /// <param name="toolName">The external tool name being called.</param>
    /// <returns>A string key used to index the rate limiter cache.</returns>
    private string BuildRateLimitKey(
        HttpContext context,
        ContextifyGatewayQuotaScope scope,
        string toolName)
    {
        var tenantId = GetTenantId(context);
        var userId = GetUserId(context);

        return scope switch
        {
            ContextifyGatewayQuotaScope.Global => "global",
            ContextifyGatewayQuotaScope.Tenant => $"tenant:{tenantId}",
            ContextifyGatewayQuotaScope.User => $"user:{tenantId}:{userId}",
            ContextifyGatewayQuotaScope.Tool => $"tool:{toolName}",
            ContextifyGatewayQuotaScope.TenantTool => $"tenant-tool:{tenantId}:{toolName}",
            ContextifyGatewayQuotaScope.UserTool => $"user-tool:{tenantId}:{userId}:{toolName}",
            _ => "global"
        };
    }

    /// <summary>
    /// Extracts the tenant identifier from the HTTP request headers.
    /// Falls back to anonymous if the header is missing.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The tenant identifier.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetTenantId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(_tenantOptions.TenantHeaderName, out var headerValue))
        {
            var value = headerValue.ToString();
            return string.IsNullOrWhiteSpace(value)
                ? ContextifyGatewayTenantResolutionOptionsEntity.AnonymousTenant
                : value;
        }

        return ContextifyGatewayTenantResolutionOptionsEntity.AnonymousTenant;
    }

    /// <summary>
    /// Extracts the user identifier from the HTTP request headers.
    /// Falls back to anonymous if the header is missing.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The user identifier.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetUserId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(_tenantOptions.UserHeaderName, out var headerValue))
        {
            var value = headerValue.ToString();
            return string.IsNullOrWhiteSpace(value)
                ? ContextifyGatewayTenantResolutionOptionsEntity.AnonymousUser
                : value;
        }

        return ContextifyGatewayTenantResolutionOptionsEntity.AnonymousUser;
    }

    /// <summary>
    /// Attempts to acquire a lease from the rate limiter for the specified key.
    /// Creates a new limiter if one doesn't exist for the key.
    /// </summary>
    /// <param name="key">The rate limit key.</param>
    /// <param name="policy">The quota policy defining rate limit parameters.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the rate limit lease result.</returns>
    private async Task<RateLimitLeaseResult> TryAcquireLeaseAsync(
        string key,
        ContextifyGatewayQuotaPolicyDto policy,
        CancellationToken cancellationToken)
    {
        var limiter = _limiterCache.GetOrCreateLimiter(
            key,
            policy.PermitLimit,
            policy.WindowMs,
            policy.QueueLimit);

        // For zero-duration windows (no queuing), use TryAcquire
        // For positive queue limits, use WaitAsync to allow queuing
        if (policy.QueueLimit == 0)
        {
            using var lease = limiter.AttemptAcquire(1);
            return CreateResultFromLease(lease);
        }

        // Use a small timeout for queued requests to prevent indefinite blocking
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            using var lease = await limiter.AcquireAsync(1, cts.Token).ConfigureAwait(false);
            return CreateResultFromLease(lease);
        }
        catch (OperationCanceledException)
        {
            // Timeout or cancellation - return a failed lease
            return new RateLimitLeaseResult(false, TimeSpan.Zero);
        }
    }

    private static RateLimitLeaseResult CreateResultFromLease(RateLimitLease lease)
    {
        var retryAfter = TimeSpan.Zero;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
        {
            retryAfter = retry;
        }

        return new RateLimitLeaseResult(lease.IsAcquired, retryAfter);
    }

    /// <summary>
    /// Writes a rate limit exceeded response to the HTTP response.
    /// Returns a JSON-RPC error response indicating rate limiting has been triggered.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="rateLimitKey">The rate limit key that was exceeded.</param>
    /// <param name="policy">The quota policy that was exceeded.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task WriteRateLimitExceededResponseAsync(
        HttpContext context,
        string rateLimitKey,
        ContextifyGatewayQuotaPolicyDto policy)
    {
        _logger.LogWarning(
            "Rate limit exceeded for key '{RateLimitKey}' with scope {Scope}. " +
            "PermitLimit: {PermitLimit}, WindowMs: {WindowMs}",
            rateLimitKey,
            policy.Scope,
            policy.PermitLimit,
            policy.WindowMs);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        // Add standard rate limit headers
        context.Response.Headers.Append("X-RateLimit-Limit", policy.PermitLimit.ToString());
        context.Response.Headers.Append("X-RateLimit-WindowMs", policy.WindowMs.ToString());
        context.Response.Headers.Append("Retry-After", "60");

        var errorResponse = new
        {
            jsonrpc = "2.0",
            error = new
            {
                code = -32001, // Custom error code for rate limiting
                message = "Rate limit exceeded. Please retry later.",
                data = new
                {
                    scope = policy.Scope.ToString(),
                    retryAfter = 60
                }
            },
            id = (object?)null
        };

        await context.Response.WriteAsJsonAsync(
            errorResponse,
            context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the rate limiter cache for use by the cleanup service.
    /// </summary>
    internal ContextifyGatewayRateLimiterCache LimiterCache => _limiterCache;
}

/// <summary>
/// Extension methods for registering the rate limit middleware in the ASP.NET Core pipeline.
/// </summary>
public static class ContextifyGatewayRateLimitMiddlewareExtensions
{
    /// <summary>
    /// Adds the Contextify Gateway rate limit middleware to the application pipeline.
    /// </summary>
    /// <param name="builder">The application builder to add middleware to.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    public static IApplicationBuilder UseContextifyGatewayRateLimit(
        this IApplicationBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.UseMiddleware<ContextifyGatewayRateLimitMiddleware>();
    }
}
