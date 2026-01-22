namespace Contextify.Gateway.Core.RateLimit;

/// <summary>
/// Defines the scope at which a rate limit quota is applied.
/// Each scope determines how the rate limit key is constructed from request context.
/// </summary>
public enum ContextifyGatewayQuotaScope
{
    /// <summary>
    /// Rate limit is applied globally across all requests, regardless of tenant, user, or tool.
    /// The rate limit key is a single global key shared by all incoming MCP calls.
    /// </summary>
    Global = 0,

    /// <summary>
    /// Rate limit is applied per tenant.
    /// The rate limit key is constructed as "{tenantId}", isolating quotas between tenants.
    /// Requests from different tenants have independent quota allowances.
    /// </summary>
    Tenant = 1,

    /// <summary>
    /// Rate limit is applied per user within a tenant.
    /// The rate limit key is constructed as "{tenantId}:{userId}", isolating quotas between users.
    /// Requests from different users have independent quota allowances.
    /// </summary>
    User = 2,

    /// <summary>
    /// Rate limit is applied per tool across all tenants and users.
    /// The rate limit key is constructed as "{externalToolName}", isolating quotas between tools.
    /// Different tools have independent quota allowances.
    /// </summary>
    Tool = 3,

    /// <summary>
    /// Rate limit is applied per tool per tenant.
    /// The rate limit key is constructed as "{tenantId}:{externalToolName}".
    /// Different tools have independent quota allowances within each tenant.
    /// </summary>
    TenantTool = 4,

    /// <summary>
    /// Rate limit is applied per tool per user within a tenant.
    /// The rate limit key is constructed as "{tenantId}:{userId}:{externalToolName}".
    /// Different tools have independent quota allowances for each user.
    /// </summary>
    UserTool = 5
}

/// <summary>
/// Data transfer object defining a quota policy for rate limiting in the gateway.
/// Contains the parameters needed to configure rate limiting behavior for a specific scope.
/// Designed for high-concurrency scenarios with millions of concurrent requests.
/// </summary>
public sealed class ContextifyGatewayQuotaPolicyDto
{
    /// <summary>
    /// Gets or sets the scope at which this quota policy is applied.
    /// Determines how the rate limit key is constructed from tenant, user, and tool identifiers.
    /// Each scope creates different isolation boundaries for quota enforcement.
    /// </summary>
    public ContextifyGatewayQuotaScope Scope { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of permits (requests) allowed within the time window.
    /// When this limit is reached, subsequent requests are rejected until permits are replenished.
    /// Must be a positive value greater than zero.
    /// </summary>
    public int PermitLimit
    {
        get => _permitLimit;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Permit limit must be greater than zero.");
            }

            _permitLimit = value;
        }
    }

    private int _permitLimit = 100;

    /// <summary>
    /// Gets or sets the duration of the sliding time window in milliseconds.
    /// Permits are replenished gradually over this window, allowing for smooth rate limiting.
    /// Must be a positive value greater than zero.
    /// </summary>
    public int WindowMs
    {
        get => _windowMs;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Time window must be greater than zero.");
            }

            _windowMs = value;
        }
    }

    private int _windowMs = 60000; // 1 minute default

    /// <summary>
    /// Gets or sets the maximum number of requests that can be queued when the limit is reached.
    /// When set to a value greater than zero, requests exceeding the permit limit may be queued
    /// instead of immediately rejected. When set to zero, no queuing occurs and requests are rejected.
    /// Must be a non-negative value (zero or positive).
    /// </summary>
    public int QueueLimit
    {
        get => _queueLimit;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Queue limit must be non-negative.");
            }

            _queueLimit = value;
        }
    }

    private int _queueLimit = 0;

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayQuotaPolicyDto class.
    /// Creates a quota policy with default values.
    /// </summary>
    public ContextifyGatewayQuotaPolicyDto()
    {
        Scope = ContextifyGatewayQuotaScope.Global;
        _permitLimit = 100;
        _windowMs = 60000;
        _queueLimit = 0;
    }

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayQuotaPolicyDto class.
    /// Creates a quota policy with the specified parameters.
    /// </summary>
    /// <param name="scope">The scope at which the quota is applied.</param>
    /// <param name="permitLimit">The maximum number of permits allowed in the window.</param>
    /// <param name="windowMs">The duration of the time window in milliseconds.</param>
    /// <param name="queueLimit">The maximum number of requests to queue when limit is reached.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when parameters are out of valid range.</exception>
    public ContextifyGatewayQuotaPolicyDto(
        ContextifyGatewayQuotaScope scope,
        int permitLimit,
        int windowMs,
        int queueLimit = 0)
    {
        if (permitLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(permitLimit), "Permit limit must be greater than zero.");
        }

        if (windowMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowMs), "Window duration must be greater than zero.");
        }

        if (queueLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueLimit), "Queue limit must be non-negative.");
        }

        Scope = scope;
        _permitLimit = permitLimit;
        _windowMs = windowMs;
        _queueLimit = queueLimit;
    }

    /// <summary>
    /// Validates the current quota policy configuration.
    /// Ensures all values are within acceptable ranges.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (PermitLimit <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(PermitLimit)} must be greater than zero. Provided value: {PermitLimit}");
        }

        if (WindowMs <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(WindowMs)} must be greater than zero. Provided value: {WindowMs}");
        }

        if (QueueLimit < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(QueueLimit)} must be non-negative. Provided value: {QueueLimit}");
        }
    }

    /// <summary>
    /// Creates a deep copy of the current quota policy instance.
    /// Useful for creating modified policies without affecting the original.
    /// </summary>
    /// <returns>A new ContextifyGatewayQuotaPolicyDto instance with copied values.</returns>
    public ContextifyGatewayQuotaPolicyDto Clone()
    {
        return new ContextifyGatewayQuotaPolicyDto(
            Scope,
            PermitLimit,
            WindowMs,
            QueueLimit);
    }
}
