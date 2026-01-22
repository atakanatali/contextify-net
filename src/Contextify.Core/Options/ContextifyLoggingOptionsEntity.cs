namespace Contextify.Core.Options;

/// <summary>
/// Configuration options for Contextify logging behavior.
/// Defines how MCP operations, tool invocations, and transport events are logged.
/// </summary>
public sealed class ContextifyLoggingOptionsEntity
{
    /// <summary>
    /// Gets or sets a value indicating whether detailed logging is enabled for MCP protocol operations.
    /// When enabled, logs all JSON-RPC messages, tool invocations, and resource access.
    /// Default value is false to avoid excessive logging in production.
    /// </summary>
    public bool EnableDetailedLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to log incoming MCP request details.
    /// Includes method names, parameters, and timestamps for debugging.
    /// Default value is false.
    /// </summary>
    public bool LogIncomingRequests { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to log outgoing MCP response details.
    /// Includes result data, errors, and processing duration.
    /// Default value is false.
    /// </summary>
    public bool LogOutgoingResponses { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to log tool invocation lifecycle events.
    /// Tracks tool registration, execution start, completion, and failures.
    /// Default value is true for operational visibility.
    /// </summary>
    public bool LogToolInvocations { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to log transport layer events.
    /// Covers HTTP requests, STDIO communication, and connection state changes.
    /// Default value is false.
    /// </summary>
    public bool LogTransportEvents { get; set; }

    /// <summary>
    /// Gets or sets the minimum log level for Contextify-specific log entries.
    /// Filters log messages based on severity (Trace, Debug, Information, Warning, Error, Critical).
    /// Default value is Information.
    /// </summary>
    public string MinimumLogLevel { get; set; } = "Information";

    /// <summary>
    /// Gets or sets a value indicating whether to include scopes in structured logging.
    /// When enabled, adds contextual scopes like ToolName, TransportType, and RequestId.
    /// Default value is true for better traceability.
    /// </summary>
    public bool IncludeScopes { get; set; } = true;

    /// <summary>
    /// Initializes a new instance with default values for Contextify logging configuration.
    /// All logging features are disabled by default except tool invocation logging and scopes.
    /// </summary>
    public ContextifyLoggingOptionsEntity()
    {
        EnableDetailedLogging = false;
        LogIncomingRequests = false;
        LogOutgoingResponses = false;
        LogToolInvocations = true;
        LogTransportEvents = false;
        MinimumLogLevel = "Information";
        IncludeScopes = true;
    }
}
