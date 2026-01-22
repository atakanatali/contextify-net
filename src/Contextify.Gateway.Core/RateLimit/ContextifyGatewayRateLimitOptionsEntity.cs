namespace Contextify.Gateway.Core.RateLimit;

/// <summary>
/// Configuration entity for rate limiting in the Contextify Gateway.
/// Defines the behavior of rate limiting including scope-based quotas and per-tool overrides.
/// Uses System.Threading.RateLimiting with sliding window rate limiting for smooth enforcement.
/// Designed for high-concurrency scenarios with millions of concurrent requests.
/// </summary>
public sealed class ContextifyGatewayRateLimitOptionsEntity
{
    /// <summary>
    /// Gets or sets a value indicating whether rate limiting is enabled.
    /// When false, all rate limiting checks are bypassed and requests proceed without quota enforcement.
    /// Default value is false, allowing rate limiting to be explicitly enabled when needed.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the default quota policy applied to all MCP calls.
    /// This policy is used when no tool-specific override matches the requested tool.
    /// Can be null if rate limiting is enabled but no default policy should be applied.
    /// </summary>
    public ContextifyGatewayQuotaPolicyDto? DefaultQuotaPolicy { get; set; }

    /// <summary>
    /// Gets the dictionary of tool-specific quota policy overrides.
    /// The key is an external tool name pattern that may contain wildcards (*).
    /// The value is the quota policy to apply when the pattern matches.
    /// More specific patterns take precedence over generic patterns.
    /// An empty or null dictionary means no tool-specific overrides are configured.
    /// </summary>
    public IDictionary<string, ContextifyGatewayQuotaPolicyDto> Overrides
    {
        get => _overrides;
        private set => _overrides = value ?? new Dictionary<string, ContextifyGatewayQuotaPolicyDto>(StringComparer.Ordinal);
    }

    private IDictionary<string, ContextifyGatewayQuotaPolicyDto> _overrides =
        new Dictionary<string, ContextifyGatewayQuotaPolicyDto>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the maximum number of rate limiter entries to maintain in memory.
    /// Each unique rate limit key (based on scope) creates a rate limiter entry.
    /// When this limit is reached, least recently used entries are evicted to control memory usage.
    /// Default value is 10,000, suitable for most multi-tenant scenarios.
    /// Must be a positive value greater than zero.
    /// </summary>
    public int MaxCacheSize
    {
        get => _maxCacheSize;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentException("Max cache size must be greater than zero.", nameof(value));
            }

            _maxCacheSize = value;
        }
    }

    private int _maxCacheSize = 10000;

    /// <summary>
    /// Gets or sets the interval at which stale rate limiter entries are cleaned up.
    /// Background cleanup removes entries for keys that haven't been accessed recently.
    /// Default value is 5 minutes, balancing memory usage with cleanup overhead.
    /// Must be a positive value greater than zero.
    /// </summary>
    public TimeSpan CleanupInterval
    {
        get => _cleanupInterval;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentException("Cleanup interval must be greater than zero.", nameof(value));
            }

            _cleanupInterval = value;
        }
    }

    private TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the idle time after which a rate limiter entry is eligible for cleanup.
    /// Entries that haven't been accessed within this duration are removed during cleanup.
    /// Default value is 10 minutes, allowing inactive tenants/users/tools to expire their quota state.
    /// Must be a positive value greater than zero.
    /// </summary>
    public TimeSpan EntryExpiration
    {
        get => _entryExpiration;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentException("Entry expiration must be greater than zero.", nameof(value));
            }

            _entryExpiration = value;
        }
    }

    private TimeSpan _entryExpiration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayRateLimitOptionsEntity class.
    /// Creates a rate limiting configuration with default values.
    /// </summary>
    public ContextifyGatewayRateLimitOptionsEntity()
    {
        Enabled = false;
        DefaultQuotaPolicy = null;
        _overrides = new Dictionary<string, ContextifyGatewayQuotaPolicyDto>(StringComparer.Ordinal);
        _maxCacheSize = 10000;
        _cleanupInterval = TimeSpan.FromMinutes(5);
        _entryExpiration = TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// Sets the tool-specific quota policy overrides.
    /// Creates a read-only snapshot of the provided dictionary to prevent external modification.
    /// </summary>
    /// <param name="overrides">The dictionary of tool pattern to quota policy mappings.</param>
    /// <exception cref="ArgumentNullException">Thrown when overrides is null.</exception>
    public void SetOverrides(IDictionary<string, ContextifyGatewayQuotaPolicyDto> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        _overrides = new Dictionary<string, ContextifyGatewayQuotaPolicyDto>(overrides, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the quota policy for a specific external tool name.
    /// Matches the tool name against override patterns, returning the default policy if no match is found.
    /// Wildcard (*) matching is supported for flexible pattern specification.
    /// </summary>
    /// <param name="externalToolName">The external tool name to find a policy for.</param>
    /// <returns>The quota policy to apply, or null if no policy matches and no default is set.</returns>
    public ContextifyGatewayQuotaPolicyDto? GetPolicyForTool(string externalToolName)
    {
        if (string.IsNullOrWhiteSpace(externalToolName))
        {
            return DefaultQuotaPolicy;
        }

        // Check for exact match first (fast path)
        if (_overrides.TryGetValue(externalToolName, out var exactMatch))
        {
            return exactMatch;
        }

        // Check for wildcard pattern matches
        foreach (var kvp in _overrides)
        {
            if (IsPatternMatch(kvp.Key, externalToolName))
            {
                return kvp.Value;
            }
        }

        return DefaultQuotaPolicy;
    }

    /// <summary>
    /// Validates the current rate limit configuration.
    /// Ensures all policies are valid and settings are within acceptable ranges.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (MaxCacheSize <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxCacheSize)} must be greater than zero. Provided value: {MaxCacheSize}");
        }

        if (CleanupInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(CleanupInterval)} must be greater than zero. Provided value: {CleanupInterval}");
        }

        if (EntryExpiration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(EntryExpiration)} must be greater than zero. Provided value: {EntryExpiration}");
        }

        // Validate default policy if present
        DefaultQuotaPolicy?.Validate();

        // Validate all override policies
        foreach (var kvp in Overrides)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
            {
                throw new InvalidOperationException(
                    "Override keys cannot be null or whitespace.");
            }

            kvp.Value?.Validate();
        }
    }

    /// <summary>
    /// Creates a deep copy of the current rate limit options instance.
    /// Useful for creating modified configurations without affecting the original.
    /// </summary>
    /// <returns>A new ContextifyGatewayRateLimitOptionsEntity instance with copied values.</returns>
    public ContextifyGatewayRateLimitOptionsEntity Clone()
    {
        var clone = new ContextifyGatewayRateLimitOptionsEntity
        {
            Enabled = Enabled,
            MaxCacheSize = MaxCacheSize,
            CleanupInterval = CleanupInterval,
            EntryExpiration = EntryExpiration
        };

        if (DefaultQuotaPolicy != null)
        {
            clone.DefaultQuotaPolicy = DefaultQuotaPolicy.Clone();
        }

        if (Overrides.Count > 0)
        {
            var clonedOverrides = new Dictionary<string, ContextifyGatewayQuotaPolicyDto>(StringComparer.Ordinal);
            foreach (var kvp in Overrides)
            {
                clonedOverrides[kvp.Key] = kvp.Value.Clone();
            }
            clone.SetOverrides(clonedOverrides);
        }

        return clone;
    }

    /// <summary>
    /// Determines whether a tool name matches a pattern containing wildcards.
    /// Supports prefix (*suffix), suffix (prefix*), and infix (pre*fix) patterns.
    /// </summary>
    /// <param name="pattern">The pattern to match against, potentially containing wildcards.</param>
    /// <param name="toolName">The tool name to test for a match.</param>
    /// <returns>True if the tool name matches the pattern; otherwise, false.</returns>
    private static bool IsPatternMatch(string pattern, string toolName)
    {
        if (!pattern.Contains('*'))
        {
            return false;
        }

        // Fast path: all wildcards
        if (pattern == "*")
        {
            return true;
        }

        // Split pattern by wildcard
        var parts = pattern.Split('*');
        if (parts.Length == 2)
        {
            // Prefix match: "weather*"
            if (string.IsNullOrEmpty(parts[0]))
            {
                return toolName.EndsWith(parts[1], StringComparison.Ordinal);
            }
            // Suffix match: "*.get_forecast"
            if (string.IsNullOrEmpty(parts[1]))
            {
                return toolName.StartsWith(parts[0], StringComparison.Ordinal);
            }
            // Infix match: "weather.*cast"
            return toolName.StartsWith(parts[0], StringComparison.Ordinal) &&
                   toolName.EndsWith(parts[1], StringComparison.Ordinal);
        }

        // Complex patterns: check each part in order
        var currentIndex = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];

            // Empty part means consecutive wildcard, skip
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            // First part must match at start
            if (i == 0 && !toolName.StartsWith(part, StringComparison.Ordinal))
            {
                return false;
            }

            // Last part must match at end
            if (i == parts.Length - 1 && !toolName.EndsWith(part, StringComparison.Ordinal))
            {
                return false;
            }

            // Middle parts must exist somewhere
            var index = toolName.IndexOf(part, currentIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            currentIndex = index + part.Length;
        }

        return true;
    }
}
