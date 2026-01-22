using System;
using System.Collections.Generic;
using System.Linq;
using Contextify.Config.Abstractions.Policy;

namespace Contextify.Config.Abstractions.Validation;

/// <summary>
/// Validation service for policy configuration objects.
/// Provides pure, deterministic validation for policy configurations.
/// Does not throw exceptions; returns structured validation results for caller handling.
/// Supports schema version checking and cross-field rule validation.
/// </summary>
public sealed class ContextifyPolicyConfigValidationService
{
    /// <summary>
    /// The maximum supported schema version for this implementation.
    /// Configurations with a higher schema version will produce an error.
    /// </summary>
    private const int MaxSupportedSchemaVersion = 1;

    /// <summary>
    /// Validates a policy configuration for correctness and compatibility.
    /// Performs schema version checking, policy validation, and cross-field rule validation.
    /// </summary>
    /// <param name="config">The policy configuration to validate.</param>
    /// <returns>A validation result containing warnings and errors.</returns>
    /// <remarks>
    /// This method is pure and deterministic; it does not modify the input configuration
    /// and does not throw exceptions for invalid configurations. The caller should
    /// inspect the returned result to determine validity.
    ///
    /// Validation checks include:
    /// - Schema version is supported
    /// - Whitelist and blacklist policies are valid
    /// - No conflicting policy combinations
    /// - Security model warnings
    /// </remarks>
    public ContextifyConfigValidationResultDto ValidatePolicyConfig(ContextifyPolicyConfigDto config)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        if (config is null)
        {
            errors.Add("Policy configuration cannot be null.");
            return ContextifyConfigValidationResultDto.WithErrors(errors);
        }

        // Validate schema version
        ValidateSchemaVersion(config.SchemaVersion, warnings, errors);

        // Validate whitelist and blacklist policies
        ValidateEndpointPolicies(config.Whitelist, "Whitelist", warnings, errors);
        ValidateEndpointPolicies(config.Blacklist, "Blacklist", warnings, errors);

        // Check for policy conflicts (same operation ID in both lists)
        ValidatePolicyConflicts(config, warnings);

        // Check for security model warnings
        ValidateSecurityModel(config, warnings, errors);

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
    /// Validates a collection of endpoint policies for correctness.
    /// Checks each policy for required fields and valid value ranges.
    /// </summary>
    private static void ValidateEndpointPolicies(
        IReadOnlyList<ContextifyEndpointPolicyDto> policies,
        string listName,
        List<string> warnings,
        List<string> errors)
    {
        for (int i = 0; i < policies.Count; i++)
        {
            var policy = policies[i];

            if (policy is null)
            {
                errors.Add($"{listName} policy at index {i} is null.");
                continue;
            }

            // Check if policy has any identifying information
            if (string.IsNullOrWhiteSpace(policy.OperationId) &&
                string.IsNullOrWhiteSpace(policy.RouteTemplate) &&
                string.IsNullOrWhiteSpace(policy.DisplayName))
            {
                errors.Add(
                    $"{listName} policy at index {i} has no identifying information. " +
                    $"At least one of OperationId, RouteTemplate, or DisplayName must be specified.");
            }

            // Validate HTTP method if route template is specified
            if (!string.IsNullOrWhiteSpace(policy.RouteTemplate) &&
                string.IsNullOrWhiteSpace(policy.HttpMethod))
            {
                warnings.Add(
                    $"{listName} policy at index {i} has a RouteTemplate but no HttpMethod. " +
                    $"The policy may not match correctly without an HTTP method.");
            }

            // Validate rate limit policy if present
            ValidateRateLimitPolicy(policy, listName, i, warnings, errors);
        }
    }

    /// <summary>
    /// Validates a rate limit policy within an endpoint policy.
    /// Checks for valid rate limit values and time windows.
    /// </summary>
    private static void ValidateRateLimitPolicy(
        ContextifyEndpointPolicyDto policy,
        string listName,
        int index,
        List<string> warnings,
        List<string> errors)
    {
        if (policy.RateLimitPolicy is null)
        {
            return;
        }

        var rateLimit = policy.RateLimitPolicy;

        // If strategy is not set, rate limiting is disabled - no validation needed
        if (string.IsNullOrWhiteSpace(rateLimit.Strategy))
        {
            return;
        }

        if (rateLimit.PermitLimit is null || rateLimit.PermitLimit.Value < 1)
        {
            errors.Add(
                $"{listName} policy at index {index} has an invalid RateLimitPolicy.PermitLimit value: {rateLimit.PermitLimit}. " +
                $"Must be at least 1 when Strategy is set.");
        }

        if (rateLimit.WindowMs is null || rateLimit.WindowMs.Value <= 0)
        {
            errors.Add(
                $"{listName} policy at index {index} has an invalid RateLimitPolicy.WindowMs value: {rateLimit.WindowMs}. " +
                $"Must be greater than zero when Strategy is set.");
        }

        // Warn about very short time windows (less than 1 second)
        if (rateLimit.WindowMs is not null && rateLimit.WindowMs.Value < 1000)
        {
            warnings.Add(
                $"{listName} policy at index {index} has a very short RateLimitPolicy.WindowMs: {rateLimit.WindowMs}ms. " +
                $"Very short windows may not provide effective rate limiting.");
        }

        // Validate queue limit if present
        if (rateLimit.QueueLimit is not null && rateLimit.QueueLimit.Value < 0)
        {
            errors.Add(
                $"{listName} policy at index {index} has an invalid RateLimitPolicy.QueueLimit value: {rateLimit.QueueLimit}. " +
                $"Cannot be negative.");
        }
    }

    /// <summary>
    /// Validates that there are no conflicting policies between whitelist and blacklist.
    /// Checks for duplicate operation IDs in both lists.
    /// </summary>
    private static void ValidatePolicyConflicts(
        ContextifyPolicyConfigDto config,
        List<string> warnings)
    {
        var whitelistOperationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blacklistOperationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var policy in config.Whitelist)
        {
            if (!string.IsNullOrWhiteSpace(policy.OperationId))
            {
                whitelistOperationIds.Add(policy.OperationId);
            }
        }

        foreach (var policy in config.Blacklist)
        {
            if (!string.IsNullOrWhiteSpace(policy.OperationId))
            {
                blacklistOperationIds.Add(policy.OperationId);
            }
        }

        // Find intersection (policies in both lists)
        var conflicts = whitelistOperationIds.Intersect(blacklistOperationIds).ToList();

        if (conflicts.Count > 0)
        {
            warnings.Add(
                $"The following operation IDs appear in both Whitelist and Blacklist: " +
                $"'{string.Join("', '", conflicts)}'. " +
                $"Blacklist takes precedence, so these operations will be denied.");
        }
    }

    /// <summary>
    /// Validates the security model configuration for potential issues.
    /// Warns about insecure default settings and validates consistency.
    /// </summary>
    private static void ValidateSecurityModel(
        ContextifyPolicyConfigDto config,
        List<string> warnings,
        List<string> errors)
    {
        if (!config.DenyByDefault)
        {
            warnings.Add(
                $"DenyByDefault is false (allow-by-default mode). " +
                $"This is less secure than deny-by-default and should only be used in development environments. " +
                $"Consider setting DenyByDefault to true for production.");
        }

        if (config.DenyByDefault && config.Whitelist.Count == 0)
        {
            errors.Add(
                $"DenyByDefault is true but Whitelist is empty. " +
                $"No endpoints will be accessible. Add policies to the Whitelist or set DenyByDefault to false.");
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
