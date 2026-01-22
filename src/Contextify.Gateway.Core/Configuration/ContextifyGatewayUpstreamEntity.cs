namespace Contextify.Gateway.Core.Configuration;

/// <summary>
/// Configuration entity for a single upstream MCP server in the gateway.
/// Defines connection settings, naming rules, and request behavior for one upstream target.
/// Each upstream represents a separate MCP HTTP endpoint that provides tools to the gateway.
/// </summary>
public sealed class ContextifyGatewayUpstreamEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for this upstream configuration.
    /// Used as the default namespace prefix and for routing requests to the correct upstream.
    /// Must be unique across all configured upstreams in the gateway.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to null or whitespace.</exception>
    public string UpstreamName
    {
        get => _upstreamName;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Upstream name cannot be null or whitespace.", nameof(value));
            }

            _upstreamName = value;
        }
    }

    private string _upstreamName = string.Empty;

    /// <summary>
    /// Gets or sets the HTTP endpoint URL for the upstream MCP server.
    /// Must be a valid absolute URI using HTTP or HTTPS scheme.
    /// All tool invocation requests for this upstream will be sent to this endpoint.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to an invalid URI.</exception>
    public Uri McpHttpEndpoint
    {
        get => _mcpHttpEndpoint;
        set
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (!value.IsAbsoluteUri || (value.Scheme != "http" && value.Scheme != "https"))
            {
                throw new ArgumentException(
                    "MCP HTTP endpoint must be an absolute URI with HTTP or HTTPS scheme.",
                    nameof(value));
            }

            _mcpHttpEndpoint = value;
        }
    }

    private Uri _mcpHttpEndpoint = null!;

    /// <summary>
    /// Gets or sets the namespace prefix for tools from this upstream.
    /// Used to construct external tool names in the format: {namespacePrefix}.{upstreamToolName}.
    /// Defaults to the UpstreamName if not explicitly set.
    /// Must contain only valid characters: letters, digits, dots, underscores, and hyphens.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to an invalid value.</exception>
    public string NamespacePrefix
    {
        get => _namespacePrefix;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Namespace prefix cannot be null or whitespace.", nameof(value));
            }

            if (!IsValidNamespacePrefix(value))
            {
                throw new ArgumentException(
                    $"Namespace prefix '{value}' contains invalid characters. " +
                    "Only letters, digits, dots, underscores, and hyphens are allowed.",
                    nameof(value));
            }

            _namespacePrefix = value;
        }
    }

    private string _namespacePrefix = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this upstream is enabled.
    /// When false, tools from this upstream are excluded from the gateway catalog.
    /// Useful for temporarily disabling an upstream without removing its configuration.
    /// Default value is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the timeout for HTTP requests to this upstream.
    /// Applies to individual tool invocation requests and catalog fetch operations.
    /// Default value is 30 seconds, suitable for most upstream scenarios.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the default HTTP headers to include in all requests to this upstream.
    /// Used for authentication, content negotiation, or other custom metadata.
    /// Headers specified here are merged with per-request headers, with per-request headers taking precedence.
    /// Can be null if no default headers are required.
    /// </summary>
    public IDictionary<string, string>? DefaultHeaders { get; set; }

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayUpstreamEntity class.
    /// Creates an upstream configuration with default values for optional properties.
    /// </summary>
    public ContextifyGatewayUpstreamEntity()
    {
        _upstreamName = string.Empty;
        _namespacePrefix = string.Empty;
        DefaultHeaders = null;
    }

    /// <summary>
    /// Validates the namespace prefix to ensure it contains only allowed characters.
    /// Valid characters are: a-z, A-Z, 0-9, dot (.), underscore (_), and hyphen (-).
    /// </summary>
    /// <param name="prefix">The namespace prefix to validate.</param>
    /// <returns>True if the prefix contains only valid characters; otherwise, false.</returns>
    private static bool IsValidNamespacePrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return false;
        }

        foreach (char c in prefix)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates the current upstream configuration.
    /// Ensures all required properties are set correctly and values are within acceptable ranges.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(UpstreamName))
        {
            throw new InvalidOperationException(
                $"{nameof(UpstreamName)} cannot be null or whitespace.");
        }

        if (McpHttpEndpoint == null)
        {
            throw new InvalidOperationException(
                $"{nameof(McpHttpEndpoint)} cannot be null.");
        }

        if (!McpHttpEndpoint.IsAbsoluteUri)
        {
            throw new InvalidOperationException(
                $"{nameof(McpHttpEndpoint)} must be an absolute URI. " +
                $"Provided value: {McpHttpEndpoint}");
        }

        if (McpHttpEndpoint.Scheme != "http" && McpHttpEndpoint.Scheme != "https")
        {
            throw new InvalidOperationException(
                $"{nameof(McpHttpEndpoint)} must use HTTP or HTTPS scheme. " +
                $"Provided scheme: {McpHttpEndpoint.Scheme}");
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(RequestTimeout)} must be greater than zero. " +
                $"Provided value: {RequestTimeout}");
        }

        if (string.IsNullOrWhiteSpace(NamespacePrefix))
        {
            throw new InvalidOperationException(
                $"{nameof(NamespacePrefix)} cannot be null or whitespace.");
        }

        if (!IsValidNamespacePrefix(NamespacePrefix))
        {
            throw new InvalidOperationException(
                $"{nameof(NamespacePrefix)} '{NamespacePrefix}' contains invalid characters. " +
                "Only letters, digits, dots, underscores, and hyphens are allowed.");
        }
    }

    /// <summary>
    /// Creates a deep copy of the current upstream configuration.
    /// Useful for creating modified configurations without affecting the original.
    /// </summary>
    /// <returns>A new ContextifyGatewayUpstreamEntity instance with copied values.</returns>
    public ContextifyGatewayUpstreamEntity Clone()
    {
        var clone = new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = UpstreamName,
            McpHttpEndpoint = new Uri(McpHttpEndpoint.ToString()),
            NamespacePrefix = NamespacePrefix,
            Enabled = Enabled,
            RequestTimeout = RequestTimeout
        };

        if (DefaultHeaders != null)
        {
            clone.DefaultHeaders = new Dictionary<string, string>(DefaultHeaders, StringComparer.Ordinal);
        }

        return clone;
    }
}
