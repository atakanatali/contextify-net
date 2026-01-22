using Microsoft.Extensions.Logging;

namespace Contextify.LoadRunner.Services;

/// <summary>
/// Contains log message definitions for source generator support.
/// Enables compile-time generation of optimized logging methods.
/// </summary>
internal static partial class Log
{
    /// <summary>
    /// Logs a JSON-RPC error received from the server.
    /// Generated at compile time using LoggerMessage attribute.
    /// </summary>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "JSON-RPC error received: Code={ErrorCode}, Message={ErrorMessage}")]
    public static partial void JsonRpcErrorReceived(
        ILogger logger,
        int errorCode,
        string errorMessage);
}
