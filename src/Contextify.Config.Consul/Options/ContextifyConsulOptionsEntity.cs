namespace Contextify.Config.Consul.Options;

/// <summary>
/// Configuration options for Consul-based policy configuration provider.
/// Defines connection settings, key paths, and reload behavior for fetching
/// Contextify policy configuration from Consul KV store.
/// </summary>
public sealed record ContextifyConsulOptionsEntity
{
    /// <summary>
    /// Gets the address of the Consul agent.
    /// Must include the protocol (http:// or https://) and port.
    /// Example: "http://localhost:8500" or "https://consul.example.com:8500"
    /// </summary>
    /// <summary>
    /// Gets or sets the address of the Consul agent.
    /// Must include the protocol (http:// or https://) and port.
    /// Example: "http://localhost:8500" or "https://consul.example.com:8500"
    /// </summary>
    public string Address { get; set; } = "http://localhost:8500";

    /// <summary>
    /// Gets or sets the ACL token for authenticated requests to Consul.
    /// Required when Consul ACLs are enabled and anonymous access is denied.
    /// Null value indicates no authentication token will be used.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the key path in Consul KV store where the policy configuration is stored.
    /// The key should contain a JSON value matching ContextifyPolicyConfigDto structure.
    /// Example: "contextify/policy/config" or "services/contextify/policy"
    /// </summary>
    public string KeyPath { get; set; } = "contextify/policy/config";

    /// <summary>
    /// Gets or sets the minimum interval between configuration reload attempts in milliseconds.
    /// Prevents excessive polling of Consul when changes are detected frequently.
    /// Also acts as a throttling mechanism for the watch callback.
    /// Default value is 1000ms (1 second). Minimum recommended value is 500ms.
    /// </summary>
    public int MinReloadIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the Consul datacenter to query for configuration.
    /// When specified, requests are routed to the specified datacenter.
    /// Null value uses the default datacenter of the Consul agent.
    /// Useful in multi-datacenter Consul deployments.
    /// </summary>
    public string? Datacenter { get; set; }

    /// <summary>
    /// Gets or sets the HTTP request timeout in milliseconds for Consul API calls.
    /// Controls how long to wait for Consul to respond before timing out.
    /// Null value uses the default Consul client timeout.
    /// Recommended value for production is 5000ms (5 seconds).
    /// </summary>
    public int? RequestTimeoutMs { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether SSL certificate validation should be skipped.
    /// When true, TLS certificate errors are ignored (useful for self-signed certificates in development).
    /// WARNING: Should never be true in production environments.
    /// Default value is false for security.
    /// </summary>
    public bool SkipSslValidation { get; set; }

    /// <summary>
    /// Validates the Consul configuration options.
    /// Ensures required properties are set and values are within acceptable ranges.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Address))
        {
            throw new InvalidOperationException(
                $"{nameof(Address)} cannot be null or empty. " +
                $"Provide a valid Consul agent address (e.g., 'http://localhost:8500').");
        }

        if (!Uri.TryCreate(Address, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"{nameof(Address)} must be a valid URI. " +
                $"Provided value: '{Address}'");
        }

        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            throw new InvalidOperationException(
                $"{nameof(Address)} must use http:// or https:// scheme. " +
                $"Provided scheme: '{uri.Scheme}'");
        }

        if (string.IsNullOrWhiteSpace(KeyPath))
        {
            throw new InvalidOperationException(
                $"{nameof(KeyPath)} cannot be null or empty. " +
                $"Provide a valid Consul KV key path for policy configuration.");
        }

        if (MinReloadIntervalMs < 500)
        {
            throw new InvalidOperationException(
                $"{nameof(MinReloadIntervalMs)} must be at least 500ms to prevent excessive polling. " +
                $"Provided value: {MinReloadIntervalMs}");
        }

        if (RequestTimeoutMs is not null && RequestTimeoutMs <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(RequestTimeoutMs)} must be greater than zero. " +
                $"Provided value: {RequestTimeoutMs}");
        }
    }

    /// <summary>
    /// Creates default options for connecting to a local Consul agent.
    /// Uses standard development settings (localhost, no auth, 1-second reload interval).
    /// </summary>
    /// <returns>A new instance with default development settings.</returns>
    public static ContextifyConsulOptionsEntity Default() =>
        new()
        {
            Address = "http://localhost:8500",
            KeyPath = "contextify/policy/config",
            MinReloadIntervalMs = 1000
        };

    /// <summary>
    /// Creates options for a production Consul deployment with ACL authentication.
    /// Configures secure connection settings appropriate for production use.
    /// </summary>
    /// <param name="address">The Consul agent address (including scheme and port).</param>
    /// <param name="token">The ACL token for authentication.</param>
    /// <param name="keyPath">The KV key path for policy configuration.</param>
    /// <param name="datacenter">Optional datacenter name.</param>
    /// <returns>A new instance with production settings.</returns>
    public static ContextifyConsulOptionsEntity Production(
        string address,
        string token,
        string keyPath = "contextify/policy/config",
        string? datacenter = null) =>
        new()
        {
            Address = address,
            Token = token,
            KeyPath = keyPath,
            Datacenter = datacenter,
            MinReloadIntervalMs = 5000,
            RequestTimeoutMs = 5000,
            SkipSslValidation = false
        };
}
