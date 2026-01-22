using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Contextify.Config.Abstractions.Policy;

/// <summary>
/// Data transfer object representing the complete policy configuration for Contextify.
/// Defines the security access model, endpoint whitelists and blacklists, and policy versioning.
/// Implements deny-by-default security where endpoints are blocked unless explicitly allowed.
/// </summary>
public sealed record ContextifyPolicyConfigDto
{
    /// <summary>
    /// Gets a value indicating whether endpoints are denied access by default.
    /// When true, all endpoints are blocked unless explicitly whitelisted.
    /// When false, endpoints are allowed unless explicitly blacklisted.
    /// Default value is true for secure-by-default behavior.
    /// This is the master switch for the security model.
    /// </summary>
    [JsonPropertyName("denyByDefault")]
    public bool DenyByDefault { get; init; } = true;

    /// <summary>
    /// Gets the collection of endpoint policies that are explicitly allowed (whitelisted).
    /// Endpoints matching any policy in this list are accessible unless blacklisted.
    /// Matching is performed using operation ID, route template + method, or display name.
    /// Whitelist is evaluated before the deny-by-default policy.
    /// Empty collection means no endpoints are explicitly whitelisted.
    /// </summary>
    [JsonPropertyName("whitelist")]
    public IReadOnlyList<ContextifyEndpointPolicyDto> Whitelist { get; init; } = [];

    /// <summary>
    /// Gets the collection of endpoint policies that are explicitly denied (blacklisted).
    /// Endpoints matching any policy in this list are blocked regardless of whitelist status.
    /// Blacklist takes precedence over whitelist for security enforcement.
    /// Empty collection means no endpoints are explicitly blacklisted.
    /// </summary>
    [JsonPropertyName("blacklist")]
    public IReadOnlyList<ContextifyEndpointPolicyDto> Blacklist { get; init; } = [];

    /// <summary>
    /// Gets the schema version of this policy configuration.
    /// Used for configuration versioning and compatibility handling.
    /// Default value is 1, representing the initial schema format.
    /// Missing values are treated as 1 for backward compatibility.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Gets the source version identifier for this policy configuration.
    /// Used for configuration change tracking, cache invalidation, and audit trails.
    /// Allows consumers to detect policy updates and reload configuration accordingly.
    /// Null value indicates no versioning is configured.
    /// </summary>
    [JsonPropertyName("sourceVersion")]
    public string? SourceVersion { get; init; }

    /// <summary>
    /// Creates a policy configuration with deny-by-default disabled (allow-by-default).
    /// All endpoints are accessible unless explicitly blacklisted.
    /// Less secure but useful for development and internal tools.
    /// </summary>
    /// <returns>A new policy configuration with deny-by-default set to false.</returns>
    public static ContextifyPolicyConfigDto AllowByDefault() =>
        new()
        {
            DenyByDefault = false
        };

    /// <summary>
    /// Creates a policy configuration with deny-by-default enabled (secure-by-default).
    /// All endpoints are blocked unless explicitly whitelisted.
    /// Recommended for production environments and external-facing services.
    /// </summary>
    /// <returns>A new policy configuration with deny-by-default set to true.</returns>
    public static ContextifyPolicyConfigDto SecureByDefault() =>
        new()
        {
            DenyByDefault = true
        };

    /// <summary>
    /// Validates the policy configuration.
    /// Ensures all endpoint policies are valid and configuration is consistent.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        var validationErrors = new List<string>();

        foreach (var policy in Whitelist)
        {
            try
            {
                policy.Validate();
            }
            catch (InvalidOperationException ex)
            {
                validationErrors.Add($"Whitelist policy '{policy.OperationId ?? policy.DisplayName ?? "(unknown)"}': {ex.Message}");
            }
        }

        foreach (var policy in Blacklist)
        {
            try
            {
                policy.Validate();
            }
            catch (InvalidOperationException ex)
            {
                validationErrors.Add($"Blacklist policy '{policy.OperationId ?? policy.DisplayName ?? "(unknown)"}': {ex.Message}");
            }
        }

        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Policy configuration validation failed:{Environment.NewLine}" +
                string.Join(Environment.NewLine, validationErrors));
        }
    }

    /// <summary>
    /// Creates a deep copy of the current policy configuration.
    /// Useful for creating modified snapshots without affecting the original configuration.
    /// </summary>
    /// <returns>A new ContextifyPolicyConfigDto instance with copied values.</returns>
    public ContextifyPolicyConfigDto DeepCopy() =>
        new()
        {
            DenyByDefault = DenyByDefault,
            Whitelist = Whitelist.ToList(),
            Blacklist = Blacklist.ToList(),
            SchemaVersion = SchemaVersion,
            SourceVersion = SourceVersion
        };
}
