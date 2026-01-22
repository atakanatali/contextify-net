namespace Contextify.Gateway.Core.Snapshot;

/// <summary>
/// Immutable snapshot entity representing a tool route in the gateway catalog.
/// Contains routing information for a single tool including namespace mapping and upstream details.
/// Used by ContextifyGatewayCatalogSnapshotEntity to provide zero-lock read access to tool routing data.
/// </summary>
public sealed class ContextifyGatewayToolRouteEntity
{
    /// <summary>
    /// Gets the external tool name exposed by the gateway.
    /// This name includes the namespace prefix and is used by clients to invoke the tool.
    /// Format is {namespacePrefix}.{upstreamToolName} when using default separator.
    /// </summary>
    public string ExternalToolName { get; }

    /// <summary>
    /// Gets the name of the upstream that provides this tool.
    /// Used to route tool invocation requests to the correct upstream endpoint.
    /// </summary>
    public string UpstreamName { get; }

    /// <summary>
    /// Gets the original tool name as known by the upstream server.
    /// Used when constructing tool invocation requests to the upstream.
    /// </summary>
    public string UpstreamToolName { get; }

    /// <summary>
    /// Gets the JSON schema describing the input parameters for this tool.
    /// Contains the validation schema that clients must follow when invoking the tool.
    /// Null if the upstream did not provide an input schema.
    /// </summary>
    public string? UpstreamInputSchemaJson { get; }

    /// <summary>
    /// Gets the human-readable description of what this tool does.
    /// Provides context to users about the tool's purpose and usage.
    /// Null if the upstream did not provide a description.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayToolRouteEntity class.
    /// Creates an immutable tool route entity with the specified routing information.
    /// </summary>
    /// <param name="externalToolName">The external tool name exposed by the gateway.</param>
    /// <param name="upstreamName">The name of the upstream providing this tool.</param>
    /// <param name="upstreamToolName">The original tool name at the upstream.</param>
    /// <param name="upstreamInputSchemaJson">The JSON schema for tool input parameters.</param>
    /// <param name="description">The human-readable description of the tool.</param>
    /// <exception cref="ArgumentNullException">Thrown when externalToolName, upstreamName, or upstreamToolName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when any string parameter is null or whitespace.</exception>
    public ContextifyGatewayToolRouteEntity(
        string externalToolName,
        string upstreamName,
        string upstreamToolName,
        string? upstreamInputSchemaJson,
        string? description)
    {
        if (string.IsNullOrWhiteSpace(externalToolName))
        {
            throw new ArgumentException("External tool name cannot be null or whitespace.", nameof(externalToolName));
        }

        if (string.IsNullOrWhiteSpace(upstreamName))
        {
            throw new ArgumentException("Upstream name cannot be null or whitespace.", nameof(upstreamName));
        }

        if (string.IsNullOrWhiteSpace(upstreamToolName))
        {
            throw new ArgumentException("Upstream tool name cannot be null or whitespace.", nameof(upstreamToolName));
        }

        ExternalToolName = externalToolName;
        UpstreamName = upstreamName;
        UpstreamToolName = upstreamToolName;
        UpstreamInputSchemaJson = upstreamInputSchemaJson;
        Description = description;
    }

    /// <summary>
    /// Creates a deep copy of the current tool route entity.
    /// Useful for creating modified routes without affecting the original snapshot.
    /// </summary>
    /// <returns>A new ContextifyGatewayToolRouteEntity instance with copied values.</returns>
    public ContextifyGatewayToolRouteEntity DeepCopy()
    {
        return new ContextifyGatewayToolRouteEntity(
            ExternalToolName,
            UpstreamName,
            UpstreamToolName,
            UpstreamInputSchemaJson,
            Description);
    }

    /// <summary>
    /// Validates the tool route entity to ensure all required fields are properly set.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExternalToolName))
        {
            throw new InvalidOperationException(
                $"{nameof(ExternalToolName)} cannot be null or whitespace.");
        }

        if (string.IsNullOrWhiteSpace(UpstreamName))
        {
            throw new InvalidOperationException(
                $"{nameof(UpstreamName)} cannot be null or whitespace.");
        }

        if (string.IsNullOrWhiteSpace(UpstreamToolName))
        {
            throw new InvalidOperationException(
                $"{nameof(UpstreamToolName)} cannot be null or whitespace.");
        }
    }
}
