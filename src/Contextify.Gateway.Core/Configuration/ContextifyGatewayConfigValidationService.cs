using System;
using System.Collections.Generic;
using System.Linq;
using Contextify.Config.Abstractions.Validation;

namespace Contextify.Gateway.Core.Configuration;

/// <summary>
/// Validation service for gateway configuration objects.
/// Provides pure, deterministic validation for gateway configurations.
/// Does not throw exceptions; returns structured validation results for caller handling.
/// Supports schema version checking and cross-field rule validation.
/// </summary>
public sealed class ContextifyGatewayConfigValidationService
{
    /// <summary>
    /// The maximum supported schema version for this implementation.
    /// Configurations with a higher schema version will produce an error.
    /// </summary>
    private const int MaxSupportedSchemaVersion = 1;

    /// <summary>
    /// Validates a gateway configuration for correctness and compatibility.
    /// Performs schema version checking, upstream validation, and operational settings validation.
    /// </summary>
    /// <param name="config">The gateway configuration to validate.</param>
    /// <returns>A validation result containing warnings and errors.</returns>
    /// <remarks>
    /// This method is pure and deterministic; it does not modify the input configuration
    /// and does not throw exceptions for invalid configurations. The caller should
    /// inspect the returned result to determine validity.
    ///
    /// Validation checks include:
    /// - Schema version is supported
    /// - Upstream configurations are valid
    /// - Tool name separator is valid
    /// - Catalog refresh interval is reasonable
    /// - No duplicate upstream names or namespace prefixes
    /// - Pattern validation for allowed/denied tool patterns
    /// </remarks>
    public ContextifyConfigValidationResultDto ValidateGatewayConfig(ContextifyGatewayOptionsEntity config)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        if (config is null)
        {
            errors.Add("Gateway configuration cannot be null.");
            return ContextifyConfigValidationResultDto.WithErrors(errors);
        }

        // Validate schema version
        ValidateSchemaVersion(config.SchemaVersion, warnings, errors);

        // Validate tool name separator
        ValidateToolNameSeparator(config.ToolNameSeparator, errors);

        // Validate catalog refresh interval
        ValidateCatalogRefreshInterval(config.CatalogRefreshInterval, warnings);

        // Validate tool patterns
        ValidateToolPatterns(config.AllowedToolPatterns, nameof(config.AllowedToolPatterns), errors);
        ValidateToolPatterns(config.DeniedToolPatterns, nameof(config.DeniedToolPatterns), errors);

        // Validate upstreams
        ValidateUpstreams(config.Upstreams, warnings, errors);

        // Check for gateway-level policy consistency
        ValidateGatewayPolicyConsistency(config, warnings);

        return BuildResult(warnings, errors);
    }

    /// <summary>
    /// Validates that the schema version is within the supported range.
    /// Missing values (0) are treated as version 1 for backward compatibility.
    /// </summary>
    private static void ValidateSchemaVersion(
        int schemaVersion,
        List<string> warnings,
        List<string> errors)
    {
        // Treat 0 as version 1 for backward compatibility (missing field)
        if (schemaVersion == 0)
        {
            warnings.Add("SchemaVersion is missing or 0; treating as version 1 for backward compatibility.");
            return;
        }

        if (schemaVersion < 1)
        {
            errors.Add($"SchemaVersion must be at least 1. Provided value: {schemaVersion}");
        }

        if (schemaVersion > MaxSupportedSchemaVersion)
        {
            errors.Add(
                $"SchemaVersion {schemaVersion} is not supported. " +
                $"Maximum supported version is {MaxSupportedSchemaVersion}. " +
                $"Update the application to support this schema version.");
        }
    }

    /// <summary>
    /// Validates the tool name separator configuration.
    /// Ensures the separator is not empty or whitespace-only.
    /// </summary>
    private static void ValidateToolNameSeparator(string separator, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(separator))
        {
            errors.Add("ToolNameSeparator cannot be null or whitespace.");
        }
    }

    /// <summary>
    /// Validates the catalog refresh interval configuration.
    /// Warns about very short or very long intervals.
    /// </summary>
    private static void ValidateCatalogRefreshInterval(TimeSpan interval, List<string> warnings)
    {
        if (interval < TimeSpan.FromSeconds(30))
        {
            warnings.Add(
                $"CatalogRefreshInterval is very short: {interval}. " +
                $"Frequent catalog refreshes may increase load on upstream servers.");
        }

        if (interval > TimeSpan.FromHours(1))
        {
            warnings.Add(
                $"CatalogRefreshInterval is very long: {interval}. " +
                $"Tool catalog changes may take a long time to propagate.");
        }
    }

    /// <summary>
    /// Validates tool pattern collections for correctness.
    /// Ensures patterns are non-empty and contain only supported wildcards.
    /// </summary>
    private static void ValidateToolPatterns(
        IReadOnlyList<string> patterns,
        string propertyName,
        List<string> errors)
    {
        for (int i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];

            if (string.IsNullOrWhiteSpace(pattern))
            {
                errors.Add($"{propertyName} cannot contain null or empty patterns. Pattern at index {i} is invalid.");
                continue;
            }

            // Check for invalid wildcard characters
            if (pattern.Contains('?') || pattern.Contains('[') || pattern.Contains(']'))
            {
                errors.Add(
                    $"{propertyName} contains invalid wildcard characters at index {i}: '{pattern}'. " +
                    $"Only '*' wildcard is supported.");
            }

            // Check for consecutive wildcards
            if (pattern.Contains("**"))
            {
                errors.Add(
                    $"{propertyName} contains invalid pattern at index {i}: '{pattern}'. " +
                    $"Consecutive wildcards (**) are not allowed.");
            }
        }
    }

    /// <summary>
    /// Validates the collection of upstream configurations.
    /// Checks for duplicates, missing required fields, and configuration consistency.
    /// </summary>
    private static void ValidateUpstreams(
        IReadOnlyList<ContextifyGatewayUpstreamEntity> upstreams,
        List<string> warnings,
        List<string> errors)
    {
        var upstreamNames = new HashSet<string>(StringComparer.Ordinal);
        var namespacePrefixes = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < upstreams.Count; i++)
        {
            var upstream = upstreams[i];

            if (upstream is null)
            {
                errors.Add($"Upstream at index {i} is null.");
                continue;
            }

            // Validate upstream name
            if (string.IsNullOrWhiteSpace(upstream.UpstreamName))
            {
                errors.Add($"Upstream at index {i} has no UpstreamName specified.");
            }
            else if (!upstreamNames.Add(upstream.UpstreamName))
            {
                errors.Add($"Duplicate upstream name detected: '{upstream.UpstreamName}'. Upstream names must be unique.");
            }

            // Validate namespace prefix
            if (string.IsNullOrWhiteSpace(upstream.NamespacePrefix))
            {
                errors.Add($"Upstream '{upstream.UpstreamName ?? "Unknown"}' has no NamespacePrefix specified.");
            }
            else if (!namespacePrefixes.Add(upstream.NamespacePrefix))
            {
                errors.Add(
                    $"Duplicate namespace prefix detected: '{upstream.NamespacePrefix}'. " +
                    $"Namespace prefixes must be unique across all upstreams.");
            }

            // Validate base URL
            if (upstream.McpHttpEndpoint == null)
            {
                errors.Add($"Upstream '{upstream.UpstreamName ?? "Unknown"}' has no McpHttpEndpoint specified.");
            }

            // Check if all upstreams are disabled
            if (!upstream.Enabled)
            {
                warnings.Add($"Upstream '{upstream.UpstreamName ?? "Unknown"}' is disabled and will not be used.");
            }
        }

        // Warn if no enabled upstreams
        if (upstreams.Count > 0 && upstreams.All(u => !u.Enabled))
        {
            errors.Add("All upstreams are disabled. At least one upstream must be enabled for gateway operation.");
        }
    }

    /// <summary>
    /// Validates gateway-level policy configuration for consistency.
    /// Checks for conflicts between deny-by-default and pattern settings.
    /// </summary>
    private static void ValidateGatewayPolicyConsistency(
        ContextifyGatewayOptionsEntity config,
        List<string> warnings)
    {
        if (config.DenyByDefault && config.AllowedToolPatterns.Count == 0)
        {
            warnings.Add(
                $"DenyByDefault is true but AllowedToolPatterns is empty. " +
                $"Tools that don't match any denied pattern will be denied. " +
                $"Consider adding patterns to AllowedToolPatterns or setting DenyByDefault to false.");
        }

        if (!config.DenyByDefault && config.DeniedToolPatterns.Count == 0 && config.AllowedToolPatterns.Count > 0)
        {
            warnings.Add(
                $"DenyByDefault is false but AllowedToolPatterns is specified while DeniedToolPatterns is empty. " +
                $"The allowed patterns will have no effect when deny-by-default is disabled.");
        }
    }

    /// <summary>
    /// Builds a validation result from the collected warnings and errors.
    /// </summary>
    private static ContextifyConfigValidationResultDto BuildResult(
        List<string> warnings,
        List<string> errors)
    {
        if (warnings.Count == 0 && errors.Count == 0)
        {
            return ContextifyConfigValidationResultDto.Success();
        }

        if (errors.Count == 0)
        {
            return ContextifyConfigValidationResultDto.WithWarnings(warnings);
        }

        if (warnings.Count == 0)
        {
            return ContextifyConfigValidationResultDto.WithErrors(errors);
        }

        return ContextifyConfigValidationResultDto.WithWarningsAndErrors(warnings, errors);
    }
}
