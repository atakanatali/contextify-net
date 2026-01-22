using System.Text.Json.Nodes;

namespace Contextify.Mcp.Abstractions.Dto;

/// <summary>
/// Data transfer object representing the response from an MCP tool invocation.
/// Contains the result of the tool execution, including output content and error information.
/// </summary>
public sealed record McpToolCallResponseDto
{
    /// <summary>
    /// Gets the content returned by the tool execution.
    /// Contains the primary output data from the tool.
    /// Can be null if the tool returns no content.
    /// </summary>
    public JsonNode? Content { get; init; }

    /// <summary>
    /// Gets a value indicating whether the tool execution was successful.
    /// True if the tool executed without errors; false otherwise.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the error message if the tool execution failed.
    /// Contains details about what went wrong during tool invocation.
    /// Null if the execution succeeded.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the error type/category if the tool execution failed.
    /// Helps categorize errors for better error handling.
    /// Null if the execution succeeded.
    /// </summary>
    public string? ErrorType { get; init; }
}
