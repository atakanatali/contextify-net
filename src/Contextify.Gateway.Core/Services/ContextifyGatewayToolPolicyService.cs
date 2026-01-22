using System.Collections.Concurrent;
using Contextify.Gateway.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Contextify.Gateway.Core.Services;

/// <summary>
/// Service for enforcing gateway-level tool access policies with wildcard pattern matching.
/// Provides deterministic allow/deny decisions based on precompiled patterns for high-performance scenarios.
/// Evaluates denied patterns first for security-first behavior, then allowed patterns.
/// Uses DenyByDefault as fallback when no patterns match.
/// Designed for millions of concurrent requests with zero-allocation pattern matching.
/// </summary>
public sealed class ContextifyGatewayToolPolicyService
{
    private readonly ILogger<ContextifyGatewayToolPolicyService> _logger;
    private readonly ConcurrentDictionary<string, CompiledPattern> _allowedPatternCache;
    private readonly ConcurrentDictionary<string, CompiledPattern> _deniedPatternCache;
    private readonly bool _denyByDefault;
    private readonly IReadOnlyList<string> _allowedPatterns;
    private readonly IReadOnlyList<string> _deniedPatterns;

    /// <summary>
    /// Gets a value indicating whether policy enforcement is active.
    /// True when any patterns are configured or DenyByDefault is true.
    /// </summary>
    public bool IsPolicyActive { get; }

    /// <summary>
    /// Initializes a new instance with gateway configuration and logger.
    /// Precompiles all patterns for fast zero-allocation matching during request processing.
    /// </summary>
    /// <param name="options">The gateway options containing policy configuration.</param>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyGatewayToolPolicyService(
        ContextifyGatewayOptionsEntity options,
        ILogger<ContextifyGatewayToolPolicyService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);

        _denyByDefault = options.DenyByDefault;
        _allowedPatterns = options.AllowedToolPatterns;
        _deniedPatterns = options.DeniedToolPatterns;

        // Precompile all patterns for fast matching
        _allowedPatternCache = new ConcurrentDictionary<string, CompiledPattern>(StringComparer.Ordinal);
        _deniedPatternCache = new ConcurrentDictionary<string, CompiledPattern>(StringComparer.Ordinal);

        // Compile allowed patterns
        foreach (var pattern in _allowedPatterns)
        {
            var compiled = CompilePattern(pattern);
            _allowedPatternCache.TryAdd(pattern, compiled);
        }

        // Compile denied patterns
        foreach (var pattern in _deniedPatterns)
        {
            var compiled = CompilePattern(pattern);
            _deniedPatternCache.TryAdd(pattern, compiled);
        }

        // Policy is active if we have patterns or deny by default is set
        IsPolicyActive = _allowedPatterns.Count > 0 || _deniedPatterns.Count > 0 || _denyByDefault;

        _logger.LogInformation(
            "Tool policy service initialized with {AllowedCount} allowed patterns, {DeniedCount} denied patterns, DenyByDefault: {DenyByDefault}",
            _allowedPatterns.Count,
            _deniedPatterns.Count,
            _denyByDefault);
    }

    /// <summary>
    /// Determines whether a tool is allowed based on gateway policy.
    /// Evaluation order: denied patterns first (security-first), then allowed patterns, then DenyByDefault.
    /// This ensures deny overrides allow in all cases.
    /// </summary>
    /// <param name="externalToolName">The external tool name to evaluate.</param>
    /// <returns>True if the tool is allowed; false if denied.</returns>
    /// <exception cref="ArgumentException">Thrown when externalToolName is null or whitespace.</exception>
    public bool IsAllowed(string externalToolName)
    {
        if (string.IsNullOrWhiteSpace(externalToolName))
        {
            throw new ArgumentException("External tool name cannot be null or whitespace.", nameof(externalToolName));
        }

        // Fast path: if no policy is active, allow everything
        if (!IsPolicyActive)
        {
            _logger.LogDebug("No policy active, allowing tool: {ToolName}", externalToolName);
            return true;
        }

        // Step 1: Check denied patterns first (security-first behavior)
        if (MatchesAnyPattern(externalToolName, _deniedPatternCache.Values))
        {
            _logger.LogDebug("Tool '{ToolName}' matched denied pattern, blocking", externalToolName);
            return false;
        }

        // Step 2: Check allowed patterns
        bool matchesAllowedPattern = MatchesAnyPattern(externalToolName, _allowedPatternCache.Values);

        if (_allowedPatterns.Count > 0)
        {
            // If we have allowed patterns, tool must match at least one
            if (matchesAllowedPattern)
            {
                _logger.LogDebug("Tool '{ToolName}' matched allowed pattern, permitting", externalToolName);
                return true;
            }
            else
            {
                _logger.LogDebug("Tool '{ToolName}' did not match any allowed pattern, blocking", externalToolName);
                return false;
            }
        }

        // Step 3: No allowed patterns configured, apply DenyByDefault
        bool result = !_denyByDefault;
        _logger.LogDebug(
            "Tool '{ToolName}' matched no patterns, applying DenyByDefault: {Result}",
            externalToolName,
            result ? "allowed" : "denied");
        return result;
    }

    /// <summary>
    /// Filters a collection of tool names to only those allowed by policy.
    /// Useful for filtering tool catalogs before returning to clients.
    /// </summary>
    /// <param name="externalToolNames">The collection of tool names to filter.</param>
    /// <returns>A read-only list of tool names that are allowed by policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when externalToolNames is null.</exception>
    public IReadOnlyList<string> FilterAllowedTools(IEnumerable<string> externalToolNames)
    {
        ArgumentNullException.ThrowIfNull(externalToolNames);

        // Fast path: if no policy is active, return all tools
        if (!IsPolicyActive)
        {
            return externalToolNames.ToList().AsReadOnly();
        }

        var allowedTools = new List<string>();

        foreach (var toolName in externalToolNames)
        {
            if (!string.IsNullOrWhiteSpace(toolName) && IsAllowed(toolName))
            {
                allowedTools.Add(toolName);
            }
        }

        _logger.LogDebug(
            "Filtered {InputCount} tools to {OutputCount} allowed tools",
            externalToolNames.Count(),
            allowedTools.Count);

        return allowedTools.AsReadOnly();
    }

    /// <summary>
    /// Checks if a tool name matches any of the provided compiled patterns.
    /// Uses fast pattern matching without allocations for high-performance scenarios.
    /// </summary>
    /// <param name="toolName">The tool name to match.</param>
    /// <param name="compiledPatterns">The collection of compiled patterns to match against.</param>
    /// <returns>True if the tool name matches any pattern; otherwise, false.</returns>
    private static bool MatchesAnyPattern(
        string toolName,
        IEnumerable<CompiledPattern> compiledPatterns)
    {
        foreach (var compiled in compiledPatterns)
        {
            if (MatchesPattern(toolName, compiled))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a tool name matches a single compiled pattern.
    /// Performs fast zero-allocation matching based on pattern type.
    /// </summary>
    /// <param name="toolName">The tool name to match.</param>
    /// <param name="compiled">The compiled pattern to match against.</param>
    /// <returns>True if the tool name matches the pattern; otherwise, false.</returns>
    private static bool MatchesPattern(string toolName, CompiledPattern compiled)
    {
        return compiled.PatternType switch
        {
            PatternType.Exact => string.Equals(toolName, compiled.Pattern, StringComparison.Ordinal),
            PatternType.Prefix => toolName.StartsWith(compiled.Prefix!, StringComparison.Ordinal) &&
                                 toolName.Length >= compiled.Prefix!.Length,
            PatternType.Suffix => toolName.EndsWith(compiled.Suffix!, StringComparison.Ordinal) &&
                                 toolName.Length >= compiled.Suffix!.Length,
            PatternType.Contains => toolName.Contains(compiled.Contains!) &&
                                    toolName.Length >= compiled.Contains!.Length,
            PatternType.Wildcard => MatchesWildcardPattern(toolName, compiled),
            _ => false
        };
    }

    /// <summary>
    /// Matches a tool name against a wildcard pattern (e.g., "prefix.*suffix").
    /// Handles patterns with single wildcards in any position.
    /// </summary>
    /// <param name="toolName">The tool name to match.</param>
    /// <param name="compiled">The compiled wildcard pattern.</param>
    /// <returns>True if the tool name matches the wildcard pattern.</returns>
    private static bool MatchesWildcardPattern(string toolName, CompiledPattern compiled)
    {
        if (compiled.Prefix is not null && compiled.Suffix is not null)
        {
            // Pattern: prefix*suffix
            return toolName.Length >= compiled.Prefix.Length + compiled.Suffix.Length &&
                   toolName.StartsWith(compiled.Prefix, StringComparison.Ordinal) &&
                   toolName.EndsWith(compiled.Suffix, StringComparison.Ordinal);
        }

        if (compiled.Prefix is not null)
        {
            // Pattern: prefix*
            return toolName.StartsWith(compiled.Prefix, StringComparison.Ordinal) &&
                   toolName.Length >= compiled.Prefix.Length;
        }

        if (compiled.Suffix is not null)
        {
            // Pattern: *suffix
            return toolName.EndsWith(compiled.Suffix, StringComparison.Ordinal) &&
                   toolName.Length >= compiled.Suffix.Length;
        }

        return false;
    }

    /// <summary>
    /// Compiles a pattern string into a fast-to-evaluate structure.
    /// Analyzes the pattern to determine its type and extracts relevant substrings.
    /// </summary>
    /// <param name="pattern">The pattern string to compile.</param>
    /// <returns>A compiled pattern structure for fast matching.</returns>
    private static CompiledPattern CompilePattern(string pattern)
    {
        int wildcardIndex = pattern.IndexOf('*');

        // No wildcards - exact match
        if (wildcardIndex < 0)
        {
            return new CompiledPattern
            {
                Pattern = pattern,
                PatternType = PatternType.Exact
            };
        }

        // Check for multiple wildcards (wildcard pattern)
        int lastWildcardIndex = pattern.LastIndexOf('*');
        if (wildcardIndex != lastWildcardIndex)
        {
            // Pattern with multiple wildcards like "prefix*middle*suffix"
            // For now, we only support single wildcard or all-wildcard patterns
            string prefix = pattern.Substring(0, wildcardIndex);
            string suffix = pattern.Substring(lastWildcardIndex + 1);

            return new CompiledPattern
            {
                Pattern = pattern,
                PatternType = PatternType.Wildcard,
                Prefix = prefix.Length > 0 ? prefix : null,
                Suffix = suffix.Length > 0 ? suffix : null
            };
        }

        // Single wildcard - determine position
        if (wildcardIndex == 0)
        {
            // Prefix wildcard: "*suffix"
            string suffix = pattern.Substring(1);
            return new CompiledPattern
            {
                Pattern = pattern,
                PatternType = PatternType.Suffix,
                Suffix = suffix
            };
        }

        if (wildcardIndex == pattern.Length - 1)
        {
            // Suffix wildcard: "prefix*"
            string prefix = pattern.Substring(0, pattern.Length - 1);
            return new CompiledPattern
            {
                Pattern = pattern,
                PatternType = PatternType.Prefix,
                Prefix = prefix
            };
        }

        // Middle wildcard: "prefix*suffix"
        string prefixPart = pattern.Substring(0, wildcardIndex);
        string suffixPart = pattern.Substring(wildcardIndex + 1);

        return new CompiledPattern
        {
            Pattern = pattern,
            PatternType = PatternType.Wildcard,
            Prefix = prefixPart,
            Suffix = suffixPart
        };
    }

    /// <summary>
    /// Internal record representing a compiled pattern for fast matching.
    /// Contains pre-extracted substrings and pattern type for zero-allocation evaluation.
    /// </summary>
    /// <param name="Pattern">The original pattern string.</param>
    /// <param name="PatternType">The type of pattern matching to perform.</param>
    /// <param name="Prefix">The prefix string for prefix/wildcard patterns.</param>
    /// <param name="Suffix">The suffix string for suffix/wildcard patterns.</param>
    /// <param name="Contains">The contained string for simple contains patterns (if needed).</param>
    private sealed record CompiledPattern
    {
        public string Pattern { get; init; } = string.Empty;
        public PatternType PatternType { get; init; }
        public string? Prefix { get; init; }
        public string? Suffix { get; init; }
        public string? Contains { get; init; }
    }

    /// <summary>
    /// Enumeration of pattern matching types.
    /// Defines the strategy for matching tool names against patterns.
    /// </summary>
    private enum PatternType
    {
        /// <summary>Exact string match (no wildcards).</summary>
        Exact,
        /// <summary>Prefix match (pattern ends with *).</summary>
        Prefix,
        /// <summary>Suffix match (pattern starts with *).</summary>
        Suffix,
        /// <summary>Contains match (wildcard in middle).</summary>
        Contains,
        /// <summary>Wildcard match with both prefix and suffix.</summary>
        Wildcard
    }
}
