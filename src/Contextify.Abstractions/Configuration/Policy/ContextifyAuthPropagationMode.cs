namespace Contextify.Config.Abstractions.Policy;

/// <summary>
/// Defines the authentication propagation mode for endpoint policy execution.
/// Controls how authentication credentials are passed from the incoming request
/// to the underlying MCP tool or downstream service.
/// </summary>
public enum ContextifyAuthPropagationMode
{
    /// <summary>
    /// Automatically infers the appropriate authentication propagation strategy
    /// based on the incoming request context and endpoint configuration.
    /// The resolver analyzes available auth headers, cookies, and endpoint metadata
    /// to select the most suitable propagation method.
    /// Default mode when no explicit mode is specified.
    /// </summary>
    Infer = 0,

    /// <summary>
    /// No authentication credentials are propagated to the downstream service.
    /// The endpoint is invoked anonymously regardless of the incoming request authentication.
    /// Useful for public endpoints or when downstream services use separate authentication mechanisms.
    /// </summary>
    None = 1,

    /// <summary>
    /// Propagates authentication using the Bearer token from the Authorization header.
    /// Extracts the bearer token from the incoming request and forwards it to downstream services.
    /// Most common for OAuth2/JWT-based authentication scenarios.
    /// </summary>
    BearerToken = 2,

    /// <summary>
    /// Propagates authentication using HTTP cookies.
    /// Forwards all relevant authentication cookies (e.g., session cookies, auth tokens)
    /// from the incoming request to downstream services.
    /// Typical for cookie-based authentication schemes and session affinity scenarios.
    /// </summary>
    Cookies = 3
}
