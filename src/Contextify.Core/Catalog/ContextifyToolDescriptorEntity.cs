using System.Text.Json;
using Contextify.Config.Abstractions.Policy;
using Contextify.Mcp.Abstractions.Dto;

namespace Contextify.Core.Catalog;

/// <summary>
/// Entity describing a tool available for invocation through the Contextify framework.
/// Contains tool metadata, input schema, endpoint information, and effective policy.
/// Serves as the primary unit of the tool catalog snapshot system.
/// </summary>
public sealed class ContextifyToolDescriptorEntity
{
    /// <summary>
    /// Gets the unique name/identifier of the tool.
    /// This name is used to invoke the tool and must be unique within the catalog.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the human-readable description of what the tool does.
    /// Helps clients understand the purpose and functionality of the tool.
    /// Null value indicates no description is available.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the JSON schema describing the input parameters accepted by the tool.
    /// Defines the structure, types, and validation rules for tool arguments.
    /// Null value indicates the tool accepts no parameters or schema is not available.
    /// </summary>
    public JsonElement? InputSchemaJson { get; }

    /// <summary>
    /// Gets the endpoint descriptor for invoking this tool.
    /// Contains routing information, HTTP method, and metadata for the tool endpoint.
    /// Null value indicates the tool is not accessible via HTTP endpoint.
    /// </summary>
    public ContextifyEndpointDescriptorEntity? EndpointDescriptor { get; }

    /// <summary>
    /// Gets the effective policy applied to this tool.
    /// Contains access control, rate limiting, timeout, and other policy settings.
    /// Null value indicates no specific policy is applied.
    /// </summary>
    public ContextifyEndpointPolicyDto? EffectivePolicy { get; }

    /// <summary>
    /// Initializes a new instance with complete tool descriptor information.
    /// </summary>
    /// <param name="toolName">The unique name/identifier of the tool.</param>
    /// <param name="description">The human-readable description.</param>
    /// <param name="inputSchemaJson">The JSON schema for input parameters.</param>
    /// <param name="endpointDescriptor">The endpoint descriptor for invocation.</param>
    /// <param name="effectivePolicy">The effective policy applied to the tool.</param>
    /// <exception cref="ArgumentNullException">Thrown when toolName is null or empty.</exception>
    /// <exception cref="ArgumentException">Thrown when toolName is whitespace.</exception>
    public ContextifyToolDescriptorEntity(
        string toolName,
        string? description,
        JsonElement? inputSchemaJson,
        ContextifyEndpointDescriptorEntity? endpointDescriptor,
        ContextifyEndpointPolicyDto? effectivePolicy)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name cannot be null or whitespace.", nameof(toolName));
        }

        ToolName = toolName;
        Description = description;
        InputSchemaJson = inputSchemaJson;
        EndpointDescriptor = endpointDescriptor;
        EffectivePolicy = effectivePolicy;
    }

    /// <summary>
    /// Creates a tool descriptor entity from an MCP tool descriptor DTO.
    /// Maps the MCP descriptor to the catalog entity format.
    /// </summary>
    /// <param name="mcpDescriptor">The MCP tool descriptor to convert from.</param>
    /// <returns>A new tool descriptor entity with mapped values.</returns>
    public static ContextifyToolDescriptorEntity FromMcpDescriptor(
        McpToolDescriptorDto mcpDescriptor)
    {
        if (mcpDescriptor is null)
        {
            throw new ArgumentNullException(nameof(mcpDescriptor));
        }

        return new ContextifyToolDescriptorEntity(
            toolName: mcpDescriptor.Name,
            description: mcpDescriptor.Description,
            inputSchemaJson: mcpDescriptor.InputSchema,
            endpointDescriptor: null,
            effectivePolicy: null);
    }

    /// <summary>
    /// Validates the tool descriptor configuration.
    /// Ensures required fields are present and values are valid.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when descriptor is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ToolName))
        {
            throw new InvalidOperationException("Tool name cannot be null or whitespace.");
        }

        EndpointDescriptor?.Validate();
        EffectivePolicy?.Validate();
    }

    /// <summary>
    /// Creates a deep copy of the current tool descriptor entity.
    /// </summary>
    /// <returns>A new tool descriptor entity with copied values.</returns>
    public ContextifyToolDescriptorEntity DeepCopy()
    {
        JsonElement? copiedSchema = null;
        if (InputSchemaJson.HasValue)
        {
            copiedSchema = JsonDocument.Parse(InputSchemaJson.Value.GetRawText()).RootElement;
        }

        ContextifyEndpointPolicyDto? copiedPolicy = null;
        if (EffectivePolicy is not null)
        {
            copiedPolicy = new ContextifyEndpointPolicyDto
            {
                RouteTemplate = EffectivePolicy.RouteTemplate,
                HttpMethod = EffectivePolicy.HttpMethod,
                OperationId = EffectivePolicy.OperationId,
                DisplayName = EffectivePolicy.DisplayName,
                ToolName = EffectivePolicy.ToolName,
                Description = EffectivePolicy.Description,
                Enabled = EffectivePolicy.Enabled,
                TimeoutMs = EffectivePolicy.TimeoutMs,
                ConcurrencyLimit = EffectivePolicy.ConcurrencyLimit,
                RateLimitPolicy = EffectivePolicy.RateLimitPolicy,
                AuthPropagationMode = EffectivePolicy.AuthPropagationMode,
                Extensions = EffectivePolicy.Extensions
            };
        }

        return new ContextifyToolDescriptorEntity(
            toolName: ToolName,
            description: Description,
            inputSchemaJson: copiedSchema,
            endpointDescriptor: EndpointDescriptor?.DeepCopy(),
            effectivePolicy: copiedPolicy);
    }
}
