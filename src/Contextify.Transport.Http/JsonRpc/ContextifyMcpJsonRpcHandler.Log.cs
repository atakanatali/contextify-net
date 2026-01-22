using Microsoft.Extensions.Logging;

namespace Contextify.Transport.Http.JsonRpc;

public sealed partial class ContextifyMcpJsonRpcHandler
{
    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Debug,
            Message = "Processing JSON-RPC request: method={Method}, id={RequestId}")]
        public static partial void ProcessingRequest(ILogger logger, string method, string? requestId);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Warning,
            Message = "Invalid JSON-RPC version: {Version}, expected 2.0")]
        public static partial void InvalidJsonRpcVersion(ILogger logger, string? version);

        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Information,
            Message = "JSON-RPC request cancelled: method={Method}, id={RequestId}")]
        public static partial void RequestCancelled(ILogger logger, string method, string? requestId);

        [LoggerMessage(
            EventId = 4,
            Level = LogLevel.Information,
            Message = "MCP runtime initialized via JSON-RPC")]
        public static partial void RuntimeInitialized(ILogger logger);

        [LoggerMessage(
            EventId = 5,
            Level = LogLevel.Debug,
            Message = "Returned {ToolCount} tools in tools/list response")]
        public static partial void ToolsListReturned(ILogger logger, int toolCount);

        [LoggerMessage(
            EventId = 6,
            Level = LogLevel.Warning,
            Message = "Tool name validation failed: {ToolName} - {Error}")]
        public static partial void ToolNameValidationFailed(ILogger logger, string? toolName, string? error);

        [LoggerMessage(
            EventId = 7,
            Level = LogLevel.Warning,
            Message = "Arguments validation failed for tool {ToolName}: {Error}")]
        public static partial void ArgumentsValidationFailed(ILogger logger, string toolName, string? error);

        [LoggerMessage(
            EventId = 8,
            Level = LogLevel.Warning,
            Message = "Tool not found or not allowed by policy: {ToolName}")]
        public static partial void ToolResultPolicyDenied(ILogger logger, string toolName);

        [LoggerMessage(
            EventId = 9,
            Level = LogLevel.Debug,
            Message = "Executing tool: {ToolName} with {ArgumentCount} arguments")]
        public static partial void ExecutingTool(ILogger logger, string toolName, int argumentCount);

        [LoggerMessage(
            EventId = 10,
            Level = LogLevel.Warning,
            Message = "Unknown JSON-RPC method: {Method}")]
        public static partial void UnknownJsonRpcMethod(ILogger logger, string method);

        [LoggerMessage(
            EventId = 11,
            Level = LogLevel.Error,
            Message = "Internal error processing JSON-RPC request: method={Method}, id={RequestId}, correlationId={CorrelationId}")]
        public static partial void InternalErrorWithCorrelation(ILogger logger, Exception ex, string method, string? requestId, string? correlationId);

        [LoggerMessage(
            EventId = 12,
            Level = LogLevel.Error,
            Message = "Internal error processing JSON-RPC request: method={Method}, id={RequestId}")]
        public static partial void InternalError(ILogger logger, Exception ex, string method, string? requestId);
    }
}
