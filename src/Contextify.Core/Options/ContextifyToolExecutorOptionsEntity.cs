using Contextify.Core.Execution;

namespace Contextify.Core.Options;

/// <summary>
/// Configuration options for the Contextify tool executor service.
/// Defines settings for HTTP client behavior, execution mode selection, and request processing.
/// </summary>
public sealed class ContextifyToolExecutorOptionsEntity
{
    /// <summary>
    /// Gets or sets the execution mode for tool invocation.
    /// Determines how and where tool endpoints are called (InProcessHttp, RemoteHttp, etc.).
    /// Default is InProcessHttp for ASP.NET Core applications.
    /// </summary>
    public ContextifyExecutionMode ExecutionMode { get; set; } =
        ContextifyExecutionMode.InProcessHttp;

    /// <summary>
    /// Gets or sets the name of the HttpClient to use from IHttpClientFactory for in-process execution.
    /// This named client should be configured with the local application base address.
    /// Default is "ContextifyInProcess" following the naming convention for named HTTP clients.
    /// </summary>
    public string HttpClientName { get; set; } = "ContextifyInProcess";

    /// <summary>
    /// Gets or sets the base address for in-process HTTP calls.
    /// Used when ExecutionMode is InProcessHttp to build the full request URI.
    /// Typically set to "http://localhost" or the actual application URL.
    /// Null indicates the executor should use the configured named client's base address.
    /// </summary>
    public string? InProcessBaseAddress { get; set; }

    /// <summary>
    /// Gets or sets the default timeout for tool execution requests in seconds.
    /// Applies to individual HTTP requests made during tool invocation.
    /// Can be overridden by tool-specific policy timeout settings.
    /// Default is 30 seconds, suitable for most tool invocations.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets a value indicating whether to include diagnostic headers in requests.
    /// When enabled, adds headers like X-Contextify-Tool-Name for tracing and debugging.
    /// Default is true for improved observability in production scenarios.
    /// </summary>
    public bool IncludeDiagnosticHeaders { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to propagate authentication context.
    /// When enabled, includes authentication tokens/headers from the original request.
    /// Default is true for secure tool invocation scenarios.
    /// </summary>
    public bool PropagateAuthContext { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to validate SSL certificates for remote execution.
    /// When disabled, allows self-signed certificates (use with caution in production).
    /// Default is true for secure remote communication.
    /// </summary>
    public bool ValidateSslCertificates { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum content length in bytes for request bodies.
    /// Prevents oversized payloads that could cause performance issues or memory pressure.
    /// Default is 10MB, suitable for most tool invocation scenarios.
    /// Set to 0 for unlimited size (not recommended for production).
    /// </summary>
    public long MaxRequestContentLengthBytes { get; set; } = 10 * 1024 * 1024; // 10MB

    /// <summary>
    /// Initializes a new instance with default tool executor settings.
    /// Default configuration uses InProcessHttp mode with standard timeout and security settings.
    /// </summary>
    public ContextifyToolExecutorOptionsEntity()
    {
        ExecutionMode = ContextifyExecutionMode.InProcessHttp;
        HttpClientName = "ContextifyInProcess";
        InProcessBaseAddress = null;
        DefaultTimeoutSeconds = 30;
        IncludeDiagnosticHeaders = true;
        PropagateAuthContext = true;
        ValidateSslCertificates = true;
        MaxRequestContentLengthBytes = 10 * 1024 * 1024; // 10MB
    }
}
