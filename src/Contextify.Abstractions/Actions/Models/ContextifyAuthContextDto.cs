namespace Contextify.Actions.Abstractions.Models;

/// <summary>
/// Data transfer object representing authentication context for tool execution.
/// Encapsulates security tokens and authentication headers needed for propagating
/// authorization information when executing tools through HTTP endpoints.
/// </summary>
public sealed record ContextifyAuthContextDto
{
    /// <summary>
    /// Gets the bearer token for OAuth2/JWT authentication schemes.
    /// When present, this token is included as an Authorization: Bearer header.
    /// Null value indicates no bearer token is available.
    /// </summary>
    public string? BearerToken { get; init; }

    /// <summary>
    /// Gets the API key for API key-based authentication.
    /// When present, this key is included in the configured header location.
    /// Null value indicates no API key is available.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Gets the header name for API key authentication.
    /// Default is "X-API-Key" when ApiKey is present but no specific header is configured.
    /// Null value indicates no API key header is configured.
    /// </summary>
    public string? ApiKeyHeaderName { get; init; }

    /// <summary>
    /// Gets additional authentication headers to include in the request.
    /// Allows for custom authentication schemes beyond bearer tokens and API keys.
    /// Empty dictionary indicates no additional headers are needed.
    /// </summary>
    public IReadOnlyDictionary<string, string> AdditionalHeaders { get; init; }

    /// <summary>
    /// Initializes a new instance with bearer token authentication.
    /// </summary>
    /// <param name="bearerToken">The bearer token for OAuth2/JWT authentication.</param>
    /// <exception cref="ArgumentException">Thrown when bearerToken is null or empty.</exception>
    public ContextifyAuthContextDto(string bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new ArgumentException("Bearer token cannot be null or empty.", nameof(bearerToken));
        }

        BearerToken = bearerToken;
        ApiKey = null;
        ApiKeyHeaderName = null;
        AdditionalHeaders = new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Initializes a new instance with API key authentication.
    /// </summary>
    /// <param name="apiKey">The API key value.</param>
    /// <param name="headerName">The header name for the API key (defaults to "X-API-Key").</param>
    /// <exception cref="ArgumentException">Thrown when apiKey is null or empty.</exception>
    public ContextifyAuthContextDto(string apiKey, string headerName)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
        }
        if (string.IsNullOrWhiteSpace(headerName))
        {
            throw new ArgumentException("Header name cannot be null or empty.", nameof(headerName));
        }

        BearerToken = null;
        ApiKey = apiKey;
        ApiKeyHeaderName = headerName;
        AdditionalHeaders = new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Initializes a new instance with custom authentication headers.
    /// </summary>
    /// <param name="additionalHeaders">The dictionary of custom authentication headers.</param>
    /// <exception cref="ArgumentNullException">Thrown when additionalHeaders is null.</exception>
    public ContextifyAuthContextDto(IReadOnlyDictionary<string, string> additionalHeaders)
    {
        ArgumentNullException.ThrowIfNull(additionalHeaders);

        BearerToken = null;
        ApiKey = null;
        ApiKeyHeaderName = null;
        AdditionalHeaders = additionalHeaders;
    }

    /// <summary>
    /// Initializes a new instance with complete authentication configuration.
    /// </summary>
    /// <param name="bearerToken">The optional bearer token.</param>
    /// <param name="apiKey">The optional API key.</param>
    /// <param name="apiKeyHeaderName">The optional API key header name.</param>
    /// <param name="additionalHeaders">The optional additional authentication headers.</param>
    public ContextifyAuthContextDto(
        string? bearerToken = null,
        string? apiKey = null,
        string? apiKeyHeaderName = null,
        IReadOnlyDictionary<string, string>? additionalHeaders = null)
    {
        BearerToken = bearerToken;
        ApiKey = apiKey;
        ApiKeyHeaderName = apiKeyHeaderName ?? (apiKey is not null ? "X-API-Key" : null);
        AdditionalHeaders = additionalHeaders ?? new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates an authentication context with a bearer token.
    /// </summary>
    /// <param name="bearerToken">The bearer token value.</param>
    /// <returns>A new authentication context configured for bearer token authentication.</returns>
    public static ContextifyAuthContextDto WithBearerToken(string bearerToken)
        => new(bearerToken);

    /// <summary>
    /// Creates an authentication context with an API key.
    /// </summary>
    /// <param name="apiKey">The API key value.</param>
    /// <param name="headerName">The header name (defaults to "X-API-Key").</param>
    /// <returns>A new authentication context configured for API key authentication.</returns>
    public static ContextifyAuthContextDto WithApiKey(string apiKey, string headerName = "X-API-Key")
        => new(apiKey, headerName);

    /// <summary>
    /// Creates an empty authentication context with no credentials.
    /// Useful for anonymous tool execution scenarios.
    /// </summary>
    /// <returns>A new authentication context with no credentials.</returns>
    public static ContextifyAuthContextDto Empty()
        => new();

    /// <summary>
    /// Applies all authentication headers from this context to the specified HTTP request message.
    /// </summary>
    /// <param name="request">The HTTP request message to apply headers to.</param>
    /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
    public void ApplyToHttpRequest(System.Net.Http.HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(BearerToken))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {BearerToken}");
        }

        if (!string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiKeyHeaderName))
        {
            request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, ApiKey);
        }

        foreach (var header in AdditionalHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}
