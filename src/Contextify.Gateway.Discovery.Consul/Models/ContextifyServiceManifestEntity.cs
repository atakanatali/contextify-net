namespace Contextify.Gateway.Discovery.Consul.Models;

/// <summary>
/// Represents the Contextify service manifest fetched from the /.well-known/contextify/manifest endpoint.
/// Provides metadata about an MCP service including its name, endpoint, and namespace configuration.
/// Services expose this manifest to enable automatic gateway discovery and configuration.
/// </summary>
public sealed class ContextifyServiceManifestEntity
{
    /// <summary>
    /// Gets or sets the service name used as the upstream identifier in the gateway.
    /// Must be unique across all discovered services for proper routing.
    /// If not specified, the Consul service name is used as the upstream name.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Gets or sets the HTTP endpoint URL for the MCP protocol.
    /// Must be a valid absolute URI or a relative path to be appended to the service address.
    /// If not specified, defaults to the root of the service address.
    /// Supports both absolute URLs (https://service.example.com/mcp) and relative paths (/mcp).
    /// </summary>
    public string? McpHttpEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the suggested namespace prefix for tools from this service.
    /// Used to construct external tool names in the format: {namespacePrefix}.{toolName}.
    /// If not specified, defaults to the ServiceName property or Consul service name.
    /// Must contain only valid characters: letters, digits, dots, underscores, and hyphens.
    /// </summary>
    public string? NamespacePrefix { get; set; }

    /// <summary>
    /// Gets or sets the version of the Contextify manifest format.
    /// Used for future compatibility and format evolution.
    /// Current version is "1.0".
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Gets or sets the description of the service for documentation and observability.
    /// Optional human-readable description of what this service provides.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the tags associated with this service for categorization.
    /// Optional collection of tags that can be used for filtering or routing decisions.
    /// Examples: ["production", "payments", "v2"].
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// Gets or sets the suggested request timeout for tool invocations to this service.
    /// Optional timeout in seconds; if not specified, gateway default timeout is used.
    /// </summary>
    public int? RequestTimeoutSeconds { get; set; }

    /// <summary>
    /// Validates the manifest entity to ensure it contains valid configuration.
    /// Checks that required fields are present and values are within acceptable ranges.
    /// </summary>
    /// <returns>True if the manifest is valid; otherwise, false.</returns>
    public bool IsValid()
    {
        // ServiceName and McpHttpEndpoint can be defaulted from Consul service info
        // So we only validate NamespacePrefix if it's explicitly provided
        if (!string.IsNullOrWhiteSpace(NamespacePrefix))
        {
            foreach (char c in NamespacePrefix)
            {
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                {
                    return false;
                }
            }
        }

        // Validate timeout if provided
        if (RequestTimeoutSeconds.HasValue && RequestTimeoutSeconds.Value <= 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Creates a deep copy of the current manifest entity.
    /// Useful for creating modified configurations without affecting the original.
    /// </summary>
    /// <returns>A new ContextifyServiceManifestEntity instance with copied values.</returns>
    public ContextifyServiceManifestEntity Clone()
    {
        var clone = new ContextifyServiceManifestEntity
        {
            ServiceName = ServiceName,
            McpHttpEndpoint = McpHttpEndpoint,
            NamespacePrefix = NamespacePrefix,
            Version = Version,
            Description = Description,
            RequestTimeoutSeconds = RequestTimeoutSeconds
        };

        if (Tags != null)
        {
            clone.Tags = (string[])Tags.Clone();
        }

        return clone;
    }
}
