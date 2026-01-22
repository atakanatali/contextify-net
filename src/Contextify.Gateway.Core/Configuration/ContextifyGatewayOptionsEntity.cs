namespace Contextify.Gateway.Core.Configuration;

/// <summary>
/// Root configuration entity for the Contextify Gateway.
/// Aggregates upstream configurations, naming conventions, and operational settings for gateway behavior.
/// Defines how the gateway connects to multiple upstream MCP servers and presents them as a unified tool catalog.
/// </summary>
public sealed class ContextifyGatewayOptionsEntity
{
    /// <summary>
    /// Gets the collection of upstream MCP server configurations.
    /// Each upstream represents a separate MCP HTTP endpoint that provides tools to the gateway.
    /// The collection is read-only to prevent external modification after initialization.
    /// </summary>
    public IReadOnlyList<ContextifyGatewayUpstreamEntity> Upstreams
    {
        get => _upstreams;
        internal set => _upstreams = value ?? throw new ArgumentNullException(nameof(value));
    }

    private IReadOnlyList<ContextifyGatewayUpstreamEntity> _upstreams = Array.Empty<ContextifyGatewayUpstreamEntity>();

    /// <summary>
    /// Gets or sets the separator used between namespace prefix and upstream tool name.
    /// Used to construct external tool names in the format: {namespacePrefix}{separator}{upstreamToolName}.
    /// Default value is "." (dot) which produces names like "weather.get_forecast".
    /// </summary>
    public string ToolNameSeparator
    {
        get => _toolNameSeparator;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Tool name separator cannot be null or whitespace.", nameof(value));
            }

            _toolNameSeparator = value;
        }
    }

    private string _toolNameSeparator = ".";

    /// <summary>
    /// Gets or sets a value indicating whether tools should be denied by default at the gateway level.
    /// When true, only tools matching allowed patterns are accessible; tools matching no pattern are denied.
    /// When false, tools from all enabled upstreams are accessible unless they match a denied pattern.
    /// Default value is false, following an allow-by-default approach for gateway-level aggregation.
    /// This property works in conjunction with AllowedToolPatterns and DeniedToolPatterns to implement
    /// a comprehensive gateway-level tool access policy with wildcard pattern matching support.
    /// </summary>
    public bool DenyByDefault { get; set; }

    /// <summary>
    /// Gets the list of allowed tool patterns at the gateway level.
    /// Tools matching any pattern in this list are considered allowed, subject to denied patterns taking precedence.
    /// Supports '*' wildcard matching at any position (prefix, suffix, or both).
    /// Examples: "payments.*", "*.read", "weather.get_*", "*admin*".
    /// An empty or null list means no explicit allow pattern filtering is applied.
    /// Denied patterns always override allowed patterns for security.
    /// </summary>
    public IReadOnlyList<string> AllowedToolPatterns
    {
        get => _allowedToolPatterns;
        internal set => _allowedToolPatterns = value ?? Array.Empty<string>();
    }

    private IReadOnlyList<string> _allowedToolPatterns = Array.Empty<string>();

    /// <summary>
    /// Gets the list of denied tool patterns at the gateway level.
    /// Tools matching any pattern in this list are unconditionally denied, regardless of allowed patterns.
    /// Supports '*' wildcard matching at any position (prefix, suffix, or both).
    /// Examples: "payments.delete*", "*password*", "*admin*".
    /// An empty or null list means no explicit deny pattern filtering is applied.
    /// Denied patterns take precedence over allowed patterns for security-first behavior.
    /// </summary>
    public IReadOnlyList<string> DeniedToolPatterns
    {
        get => _deniedToolPatterns;
        internal set => _deniedToolPatterns = value ?? Array.Empty<string>();
    }

    private IReadOnlyList<string> _deniedToolPatterns = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the interval at which the gateway refreshes tool catalogs from upstreams.
    /// Determines how frequently the gateway polls upstream servers for tool catalog changes.
    /// Default value is 5 minutes, balancing freshness with load on upstream servers.
    /// </summary>
    public TimeSpan CatalogRefreshInterval
    {
        get => _catalogRefreshInterval;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "Catalog refresh interval must be greater than zero.",
                    nameof(value));
            }

            _catalogRefreshInterval = value;
        }
    }

    private TimeSpan _catalogRefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayOptionsEntity class.
    /// Creates a gateway configuration with default values for optional properties.
    /// </summary>
    public ContextifyGatewayOptionsEntity()
    {
        _upstreams = Array.Empty<ContextifyGatewayUpstreamEntity>();
        _toolNameSeparator = ".";
        DenyByDefault = false;
        _allowedToolPatterns = Array.Empty<string>();
        _deniedToolPatterns = Array.Empty<string>();
        _catalogRefreshInterval = TimeSpan.FromMinutes(5);
        SchemaVersion = 1;
    }

    /// <summary>
    /// Gets or sets the schema version of this gateway configuration.
    /// Used for configuration versioning and compatibility handling.
    /// Default value is 1, representing the initial schema format.
    /// Missing values are treated as 1 for backward compatibility.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Sets the upstreams configuration for the gateway.
    /// Creates a read-only snapshot of the provided collection to prevent external modification.
    /// </summary>
    /// <param name="upstreams">The collection of upstream configurations to set.</param>
    /// <exception cref="ArgumentNullException">Thrown when upstreams is null.</exception>
    public void SetUpstreams(IReadOnlyList<ContextifyGatewayUpstreamEntity> upstreams)
    {
        Upstreams = upstreams ?? throw new ArgumentNullException(nameof(upstreams));
    }

    /// <summary>
    /// Validates the current gateway configuration.
    /// Ensures all upstreams are valid and settings are within acceptable ranges.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        // Validate separator is not empty
        if (string.IsNullOrWhiteSpace(ToolNameSeparator))
        {
            throw new InvalidOperationException(
                $"{nameof(ToolNameSeparator)} cannot be null or whitespace.");
        }

        // Validate refresh interval
        if (CatalogRefreshInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(CatalogRefreshInterval)} must be greater than zero. " +
                $"Provided value: {CatalogRefreshInterval}");
        }

        // Validate tool policy patterns
        ValidateToolPatterns(AllowedToolPatterns, nameof(AllowedToolPatterns));
        ValidateToolPatterns(DeniedToolPatterns, nameof(DeniedToolPatterns));

        // Validate each upstream configuration
        var upstreamNames = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < Upstreams.Count; i++)
        {
            var upstream = Upstreams[i];

            if (upstream == null)
            {
                throw new InvalidOperationException(
                    $"Upstream at index {i} is null.");
            }

            // Validate the upstream itself
            upstream.Validate();

            // Check for duplicate upstream names
            if (!upstreamNames.Add(upstream.UpstreamName))
            {
                throw new InvalidOperationException(
                    $"Duplicate upstream name detected: '{upstream.UpstreamName}'. " +
                    "Upstream names must be unique across all configured upstreams.");
            }

            // Check for duplicate namespace prefixes
            if (Upstreams.Count(u => u != null && u.NamespacePrefix == upstream.NamespacePrefix) > 1)
            {
                throw new InvalidOperationException(
                    $"Duplicate namespace prefix detected: '{upstream.NamespacePrefix}'. " +
                    "Namespace prefixes must be unique across all configured upstreams.");
            }
        }
    }

    /// <summary>
    /// Creates a deep copy of the current gateway options instance.
    /// Useful for creating modified snapshots without affecting the original configuration.
    /// </summary>
    /// <returns>A new ContextifyGatewayOptionsEntity instance with copied values.</returns>
    public ContextifyGatewayOptionsEntity Clone()
    {
        var clone = new ContextifyGatewayOptionsEntity
        {
            ToolNameSeparator = ToolNameSeparator,
            DenyByDefault = DenyByDefault,
            AllowedToolPatterns = AllowedToolPatterns.ToList(),
            DeniedToolPatterns = DeniedToolPatterns.ToList(),
            CatalogRefreshInterval = CatalogRefreshInterval,
            SchemaVersion = SchemaVersion
        };

        // Clone each upstream
        if (Upstreams.Count > 0)
        {
            var clonedUpstreams = new List<ContextifyGatewayUpstreamEntity>(Upstreams.Count);

            foreach (var upstream in Upstreams)
            {
                clonedUpstreams.Add(upstream.Clone());
            }

            clone.SetUpstreams(clonedUpstreams);
        }

        return clone;
    }

    /// <summary>
    /// Gets all enabled upstreams from the configuration.
    /// Filters the upstreams collection to return only those where Enabled is true.
    /// </summary>
    /// <returns>A read-only list of enabled upstream configurations.</returns>
    public IReadOnlyList<ContextifyGatewayUpstreamEntity> GetEnabledUpstreams()
    {
        return Upstreams
            .Where(u => u.Enabled)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets an upstream configuration by its name.
    /// </summary>
    /// <param name="upstreamName">The name of the upstream to retrieve.</param>
    /// <returns>The upstream configuration if found; otherwise, null.</returns>
    public ContextifyGatewayUpstreamEntity? GetUpstreamByName(string upstreamName)
    {
        if (string.IsNullOrWhiteSpace(upstreamName))
        {
            return null;
        }

        return Upstreams.FirstOrDefault(u => u.UpstreamName == upstreamName);
    }

    /// <summary>
    /// Gets an upstream configuration by its namespace prefix.
    /// </summary>
    /// <param name="namespacePrefix">The namespace prefix to search for.</param>
    /// <returns>The upstream configuration if found; otherwise, null.</returns>
    public ContextifyGatewayUpstreamEntity? GetUpstreamByNamespacePrefix(string namespacePrefix)
    {
        if (string.IsNullOrWhiteSpace(namespacePrefix))
        {
            return null;
        }

        return Upstreams.FirstOrDefault(u => u.NamespacePrefix == namespacePrefix);
    }

    /// <summary>
    /// Sets the allowed tool patterns for gateway-level policy.
    /// Creates a read-only snapshot of the provided collection to prevent external modification.
    /// </summary>
    /// <param name="allowedToolPatterns">The collection of allowed tool patterns to set.</param>
    /// <exception cref="ArgumentNullException">Thrown when allowedToolPatterns is null.</exception>
    public void SetAllowedToolPatterns(IReadOnlyList<string> allowedToolPatterns)
    {
        ArgumentNullException.ThrowIfNull(allowedToolPatterns);
        AllowedToolPatterns = allowedToolPatterns;
    }

    /// <summary>
    /// Sets the denied tool patterns for gateway-level policy.
    /// Creates a read-only snapshot of the provided collection to prevent external modification.
    /// </summary>
    /// <param name="deniedToolPatterns">The collection of denied tool patterns to set.</param>
    /// <exception cref="ArgumentNullException">Thrown when deniedToolPatterns is null.</exception>
    public void SetDeniedToolPatterns(IReadOnlyList<string> deniedToolPatterns)
    {
        ArgumentNullException.ThrowIfNull(deniedToolPatterns);
        DeniedToolPatterns = deniedToolPatterns;
    }

    /// <summary>
    /// Validates tool policy patterns for correct wildcard usage.
    /// Ensures patterns are non-null, non-empty, and contain only valid wildcards.
    /// </summary>
    /// <param name="patterns">The collection of patterns to validate.</param>
    /// <param name="paramName">The parameter name for error reporting.</param>
    /// <exception cref="InvalidOperationException">Thrown when a pattern is invalid.</exception>
    private static void ValidateToolPatterns(IReadOnlyList<string> patterns, string paramName)
    {
        for (int i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];

            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new InvalidOperationException(
                    $"{paramName} cannot contain null or empty patterns. Pattern at index {i} is invalid.");
            }

            // Check for invalid wildcard characters (only '*' is supported)
            if (pattern.IndexOf('?') >= 0 || pattern.IndexOf('[') >= 0 || pattern.IndexOf(']') >= 0)
            {
                throw new InvalidOperationException(
                    $"{paramName} contains invalid wildcard characters at index {i}: '{pattern}'. " +
                    "Only '*' wildcard is supported.");
            }

            // Validate that wildcards are not consecutive (**) as this could cause confusion
            if (pattern.Contains("**"))
            {
                throw new InvalidOperationException(
                    $"{paramName} contains invalid pattern at index {i}: '{pattern}'. " +
                    "Consecutive wildcards (**) are not allowed.");
            }
        }
    }
}
