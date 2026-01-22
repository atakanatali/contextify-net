namespace Contextify.Gateway.Discovery.Consul.Configuration;

/// <summary>
/// Configuration entity for Consul-based gateway upstream discovery.
/// Defines how to connect to Consul and filter discovered services for gateway integration.
/// Enables automatic upstream registration from Consul service catalog with manifest crawling.
/// </summary>
public sealed class ContextifyGatewayConsulDiscoveryOptionsEntity
{
    /// <summary>
    /// Gets or sets the Consul agent HTTP address.
    /// Must be a valid absolute URI using HTTP or HTTPS scheme.
    /// Default value is "http://localhost:8500" for local development.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to an invalid URI.</exception>
    public Uri Address
    {
        get => _address;
        set
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (!value.IsAbsoluteUri || (value.Scheme != "http" && value.Scheme != "https"))
            {
                throw new ArgumentException(
                    "Address must be an absolute URI with HTTP or HTTPS scheme.",
                    nameof(value));
            }

            _address = value;
        }
    }

    private Uri _address = new Uri("http://localhost:8500");

    /// <summary>
    /// Gets or sets the Consul ACL token for authenticated requests.
    /// Optional token used when Consul ACLs are enabled for access control.
    /// Null or empty string indicates no authentication token is required.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the tag filter for discovering services in Consul.
    /// Only services with this tag will be discovered and configured as upstreams.
    /// Null or empty string means no tag filtering is applied.
    /// Useful for distinguishing Contextify-enabled services from other Consul services.
    /// </summary>
    public string? ServiceTagFilter { get; set; }

    /// <summary>
    /// Gets or sets the service name prefix filter for Consul catalog queries.
    /// Only services whose names start with this prefix will be discovered.
    /// Null or empty string means no prefix filtering is applied.
    /// Examples: "ctx-", "mcp-", "contextify-" for service naming conventions.
    /// </summary>
    public string? ServiceNamePrefix { get; set; }

    /// <summary>
    /// Gets or sets the polling interval for refreshing discovered services from Consul.
    /// Determines how frequently the discovery provider queries Consul for changes.
    /// Default value is 30 seconds, balancing freshness with load on Consul.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to a non-positive value.</exception>
    public TimeSpan PollInterval
    {
        get => _pollInterval;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "Poll interval must be greater than zero.",
                    nameof(value));
            }

            _pollInterval = value;
        }
    }

    private TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the HTTP request timeout for Consul API calls.
    /// Applies to catalog queries and health checks during discovery.
    /// Default value is 10 seconds, suitable for most Consul deployments.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to a non-positive value.</exception>
    public TimeSpan RequestTimeout
    {
        get => _requestTimeout;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "Request timeout must be greater than zero.",
                    nameof(value));
            }

            _requestTimeout = value;
        }
    }

    private TimeSpan _requestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the path to the Contextify manifest endpoint on discovered services.
    /// Relative path appended to each service instance address to fetch its configuration.
    /// Default value is "/.well-known/contextify/manifest" following RFC 8615 conventions.
    /// The manifest contains service name, MCP endpoint, and namespace prefix configuration.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to null or whitespace.</exception>
    public string ManifestPath
    {
        get => _manifestPath;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Manifest path cannot be null or whitespace.", nameof(value));
            }

            if (!value.StartsWith("/"))
            {
                throw new ArgumentException(
                    "Manifest path must start with '/'.",
                    nameof(value));
            }

            _manifestPath = value;
        }
    }

    private string _manifestPath = "/.well-known/contextify/manifest";

    /// <summary>
    /// Gets or sets the datacenter name for Consul catalog queries.
    /// Limits discovery to services in the specified Consul datacenter.
    /// Null or empty string queries the local agent's datacenter.
    /// Useful for multi-datacenter deployments where gateway should only see local services.
    /// </summary>
    public string? Datacenter { get; set; }

    /// <summary>
    /// Gets or sets the default namespace prefix for discovered upstreams.
    /// Used when a service's manifest does not specify a namespace prefix.
    /// Null or empty string means the upstream name is used as the namespace prefix.
    /// </summary>
    public string? DefaultNamespacePrefix { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to fetch manifests from service instances.
    /// When true, the discovery provider fetches the manifest from each discovered service.
    /// When false, only basic service information is used for upstream configuration.
    /// Default value is true to enable full Contextify manifest integration.
    /// </summary>
    public bool FetchManifests { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to use HTTPS for manifest fetching.
    /// When true, manifests are fetched using HTTPS scheme from discovered services.
    /// When false, HTTP scheme is used for manifest requests.
    /// Default value is false (HTTP) for compatibility with local development.
    /// </summary>
    public bool UseHttpsForManifests { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent manifest fetch operations.
    /// Limits parallelism when fetching manifests from multiple service instances.
    /// Default value is 10 to prevent overwhelming services with concurrent requests.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to a non-positive value.</exception>
    public int MaxConcurrentManifestFetches
    {
        get => _maxConcurrentManifestFetches;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentException(
                    "Max concurrent manifest fetches must be greater than zero.",
                    nameof(value));
            }

            _maxConcurrentManifestFetches = value;
        }
    }

    private int _maxConcurrentManifestFetches = 10;

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayConsulDiscoveryOptionsEntity class.
    /// Creates a Consul discovery configuration with default values for optional properties.
    /// </summary>
    public ContextifyGatewayConsulDiscoveryOptionsEntity()
    {
        _address = new Uri("http://localhost:8500");
        Token = null;
        ServiceTagFilter = null;
        ServiceNamePrefix = null;
        _pollInterval = TimeSpan.FromSeconds(30);
        _requestTimeout = TimeSpan.FromSeconds(10);
        _manifestPath = "/.well-known/contextify/manifest";
        Datacenter = null;
        DefaultNamespacePrefix = null;
        FetchManifests = true;
        UseHttpsForManifests = false;
        _maxConcurrentManifestFetches = 10;
    }

    /// <summary>
    /// Validates the current Consul discovery configuration.
    /// Ensures all required properties are set correctly and values are within acceptable ranges.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (Address == null)
        {
            throw new InvalidOperationException(
                $"{nameof(Address)} cannot be null.");
        }

        if (!Address.IsAbsoluteUri)
        {
            throw new InvalidOperationException(
                $"{nameof(Address)} must be an absolute URI. " +
                $"Provided value: {Address}");
        }

        if (Address.Scheme != "http" && Address.Scheme != "https")
        {
            throw new InvalidOperationException(
                $"{nameof(Address)} must use HTTP or HTTPS scheme. " +
                $"Provided scheme: {Address.Scheme}");
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(PollInterval)} must be greater than zero. " +
                $"Provided value: {PollInterval}");
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(RequestTimeout)} must be greater than zero. " +
                $"Provided value: {RequestTimeout}");
        }

        if (string.IsNullOrWhiteSpace(ManifestPath))
        {
            throw new InvalidOperationException(
                $"{nameof(ManifestPath)} cannot be null or whitespace.");
        }

        if (!ManifestPath.StartsWith("/"))
        {
            throw new InvalidOperationException(
                $"{nameof(ManifestPath)} must start with '/'. " +
                $"Provided value: {ManifestPath}");
        }

        if (MaxConcurrentManifestFetches <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxConcurrentManifestFetches)} must be greater than zero. " +
                $"Provided value: {MaxConcurrentManifestFetches}");
        }
    }
}
