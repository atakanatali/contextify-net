namespace Contextify.Gateway.Core.Configuration;

/// <summary>
/// Configuration entity for tenant and user resolution in multi-tenant gateway scenarios.
/// Defines the HTTP header names used to extract tenant and user identifiers from incoming requests.
/// These identifiers are used for rate limiting, quota enforcement, and request routing in multi-tenant environments.
/// Designed for high-concurrency scenarios with millions of concurrent requests.
/// </summary>
public sealed class ContextifyGatewayTenantResolutionOptionsEntity
{
    /// <summary>
    /// Gets or sets the HTTP header name that contains the tenant identifier.
    /// This header is used to identify which tenant a request belongs to for rate limiting purposes.
    /// Default value is "X-Tenant-Id".
    /// The tenant ID is combined with other identifiers (user, tool) to create rate limit keys.
    /// When the header is missing, requests are mapped to an "anonymous" tenant key.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to null or whitespace.</exception>
    public string TenantHeaderName
    {
        get => _tenantHeaderName;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Tenant header name cannot be null or whitespace.", nameof(value));
            }

            _tenantHeaderName = value;
        }
    }

    private string _tenantHeaderName = "X-Tenant-Id";

    /// <summary>
    /// Gets or sets the HTTP header name that contains the user identifier.
    /// This header is used to identify which user within a tenant is making the request.
    /// Default value is "X-User-Id".
    /// The user ID is combined with tenant and tool identifiers to create rate limit keys for user-scoped quotas.
    /// When the header is missing, requests are mapped to an "anonymous" user key.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to null or whitespace.</exception>
    public string UserHeaderName
    {
        get => _userHeaderName;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("User header name cannot be null or whitespace.", nameof(value));
            }

            _userHeaderName = value;
        }
    }

    private string _userHeaderName = "X-User-Id";

    /// <summary>
    /// Gets the constant value used for anonymous tenant identification.
    /// When the tenant header is missing from a request, this value is used as the tenant identifier.
    /// </summary>
    public const string AnonymousTenant = "anonymous";

    /// <summary>
    /// Gets the constant value used for anonymous user identification.
    /// When the user header is missing from a request, this value is used as the user identifier.
    /// </summary>
    public const string AnonymousUser = "anonymous";

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayTenantResolutionOptionsEntity class.
    /// Creates a tenant resolution configuration with default header names.
    /// </summary>
    public ContextifyGatewayTenantResolutionOptionsEntity()
    {
        _tenantHeaderName = "X-Tenant-Id";
        _userHeaderName = "X-User-Id";
    }

    /// <summary>
    /// Validates the current tenant resolution configuration.
    /// Ensures all header names are properly configured and non-empty.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TenantHeaderName))
        {
            throw new InvalidOperationException(
                $"{nameof(TenantHeaderName)} cannot be null or whitespace.");
        }

        if (string.IsNullOrWhiteSpace(UserHeaderName))
        {
            throw new InvalidOperationException(
                $"{nameof(UserHeaderName)} cannot be null or whitespace.");
        }
    }

    /// <summary>
    /// Creates a deep copy of the current tenant resolution options instance.
    /// Useful for creating modified configurations without affecting the original.
    /// </summary>
    /// <returns>A new ContextifyGatewayTenantResolutionOptionsEntity instance with copied values.</returns>
    public ContextifyGatewayTenantResolutionOptionsEntity Clone()
    {
        return new ContextifyGatewayTenantResolutionOptionsEntity
        {
            TenantHeaderName = TenantHeaderName,
            UserHeaderName = UserHeaderName
        };
    }
}
