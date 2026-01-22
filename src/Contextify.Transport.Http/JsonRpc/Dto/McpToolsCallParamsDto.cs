using System.Text.Json.Nodes;

namespace Contextify.Transport.Http.JsonRpc.Dto;

/// <summary>
/// Data transfer object representing parameters for the tools/call method.
/// Used to execute a specific tool with its arguments.
/// </summary>
public sealed record McpToolsCallParamsDto
{
    /// <summary>
    /// Gets the name of the tool to invoke.
    /// Must match one of the tool names returned by the tools/list method.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the arguments to pass to the tool.
    /// Structure must match the input schema defined in the tool descriptor.
    /// Can be null if the tool accepts no parameters.
    /// </summary>
    public JsonObject? Arguments { get; init; }
}

/// <summary>
/// Data transfer object representing the response from tools/call method.
/// Contains the result of tool execution including content and error information.
/// </summary>
public sealed record McpToolsCallResultDto
{
    /// <summary>
    /// Gets the content returned by the tool execution.
    /// Contains the primary output data from the tool.
    /// Null value indicates no content was returned.
    /// </summary>
    public JsonNode? Content { get; init; }

    /// <summary>
    /// Gets a value indicating whether the tool execution was successful.
    /// True if the tool executed without errors; false otherwise.
    /// </summary>
    public required bool IsError { get; init; }
}
