namespace Contextify.Core;

/// <summary>
/// Defines the transport mode for MCP (Model Context Protocol) communication.
/// Determines how the Contextify runtime communicates with MCP clients and servers.
/// </summary>
public enum ContextifyTransportMode
{
    /// <summary>
    /// Automatically selects the appropriate transport mode based on the hosting environment.
    /// In ASP.NET Core applications, defaults to HTTP transport.
    /// In console applications, defaults to STDIO transport.
    /// </summary>
    Auto,

    /// <summary>
    /// Uses HTTP/HTTPS transport for MCP communication.
    /// Suitable for web applications and REST API scenarios.
    /// Supports both self-hosted and IIS/Kestrel hosting models.
    /// </summary>
    Http,

    /// <summary>
    /// Uses Standard Input/Output (STDIO) transport for MCP communication.
    /// Suitable for command-line tools and local process communication.
    /// Commonly used for CLI-based MCP servers and desktop integration.
    /// </summary>
    Stdio,

    /// <summary>
    /// Enables both HTTP and STDIO transports simultaneously.
    /// Allows the application to handle MCP communication through multiple channels.
    /// Useful for hybrid scenarios supporting both web and CLI interactions.
    /// </summary>
    Both
}
