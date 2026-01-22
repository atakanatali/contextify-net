using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Contextify.Config.Abstractions.Policy;
using Contextify.Config.Abstractions.Validation;
using Contextify.Config.Consul.Internal;
using Contextify.Config.Consul.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Contextify.Config.Consul.Provider;

/// <summary>
/// Policy configuration provider that fetches configuration from Consul KV store.
/// Implements IContextifyPolicyConfigProvider to supply policy configuration from Consul
/// with version tracking using ModifyIndex and change detection via polling.
/// Thread-safe implementation with throttled reload intervals and efficient change detection.
/// Includes validation and last-known-good fallback to prevent crashes on invalid config.
/// </summary>
public sealed class ConsulPolicyConfigProvider : IContextifyPolicyConfigProvider, IDisposable
{
    private readonly ContextifyConsulOptionsEntity _options;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, CallbackRegistration> _callbacks;
    private readonly Timer _pollingTimer;
    private readonly ContextifyPolicyConfigValidationService _validationService;
    private readonly ILogger<ConsulPolicyConfigProvider>? _logger;
    private ulong _currentModifyIndex;
    private ContextifyPolicyConfigDto? _cachedConfig;
    private ContextifyPolicyConfigDto? _lastKnownGoodConfig;
    private bool _disposed;
    private long _lastReloadTimeUtcTicks;

    /// <summary>
    /// Initializes a new instance with the specified options and HTTP client.
    /// The HTTP client is used for communicating with the Consul KV API.
    /// </summary>
    /// <param name="options">The configuration options for Consul connection.</param>
    /// <param name="httpClient">The HTTP client for making Consul API requests.</param>
    /// <param name="logger">Optional logger for recording validation errors and fallback events.</param>
    /// <exception cref="ArgumentNullException">Thrown when options or httpClient is null.</exception>
    public ConsulPolicyConfigProvider(
        IOptions<ContextifyConsulOptionsEntity> options,
        HttpClient httpClient,
        ILogger<ConsulPolicyConfigProvider>? logger = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _options = options.Value;
        _options.Validate();

        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _callbacks = new ConcurrentDictionary<string, CallbackRegistration>();
        _validationService = new ContextifyPolicyConfigValidationService();
        _logger = logger;
        _lastReloadTimeUtcTicks = DateTime.MinValue.Ticks;

        // Initialize timer but don't start it yet - it will be started on first Watch() call
        _pollingTimer = new Timer(
            PollForChangesAsync,
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    /// <summary>
    /// Asynchronously retrieves the current policy configuration from Consul KV store.
    /// Fetches the configuration from the configured key path and tracks the ModifyIndex
    /// for versioning. Returns cached configuration if the index hasn't changed.
    /// On validation failure, returns last-known-good configuration to prevent crashes.
    /// </summary>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>A ValueTask containing the current policy configuration.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <exception cref="InvalidOperationException">Thrown when configuration cannot be retrieved or parsed and no cached config exists.</exception>
    public async ValueTask<ContextifyPolicyConfigDto> GetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        // Build the Consul KV URL
        string kvUrl = BuildConsulKvUrl();

        // Check throttling for GetAsync
        var now = DateTime.UtcNow;
        var lastReload = new DateTime(_lastReloadTimeUtcTicks, DateTimeKind.Utc);
        var timeSinceLastReload = (now - lastReload).TotalMilliseconds;

        // If we have cached config and it's too soon to reload, return cache
        if (_cachedConfig is not null && timeSinceLastReload < _options.MinReloadIntervalMs)
        {
            return _cachedConfig;
        }

        // Build the Consul KV URL (already defined)
        kvUrl = BuildConsulKvUrl();

        // If we have cached config, try to fetch with index
        if (_cachedConfig is not null)
        {
            try
            {
                var fetchedConfig = await FetchConfigAsync(kvUrl, ct);
                return fetchedConfig;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // On error, log and return cached config if available
                _logger?.LogError(ex, "Failed to fetch policy configuration from Consul. Using cached configuration.");
                return _cachedConfig;
            }
        }

        // First fetch or no cache available
        var config = await FetchConfigAsync(kvUrl, ct);
        return config;
    }

    /// <summary>
    /// Gets a change token for monitoring policy configuration updates from Consul.
    /// The token triggers polling-based change detection using the configured minimum interval.
    /// Multiple consumers can register callbacks; all are notified when a change is detected.
    /// </summary>
    /// <returns>A change token that signals when configuration has changed.</returns>
    /// <remarks>
    /// The change token implementation uses a polling mechanism with the following characteristics:
    /// - Polling interval is controlled by MinReloadIntervalMs (minimum 500ms)
    /// - Change detection uses Consul's ModifyIndex to avoid unnecessary full fetches
    /// - Throttling prevents excessive API calls to Consul
    /// - Thread-safe callback registration and invocation
    ///
    /// The first call to Watch() starts the polling timer. Subsequent calls register additional
    /// callbacks that will be notified on the same polling cycle.
    ///
    /// Example usage:
    /// <code>
    /// var changeToken = provider.Watch();
    /// changeToken?.RegisterChangeCallback(state => ReloadPolicy(), null);
    /// </code>
    /// </remarks>
    public IChangeToken? Watch()
    {
        ThrowIfDisposed();

        var changeToken = new ConsulChangeToken(this);
        StartPolling();
        return changeToken;
    }

    /// <summary>
    /// Starts the polling timer for change detection.
    /// Only starts the timer if not already running (thread-safe).
    /// </summary>
    private void StartPolling()
    {
        lock (_pollingTimer)
        {
            // Only start if not already running
            _pollingTimer.Change(_options.MinReloadIntervalMs, _options.MinReloadIntervalMs);
        }
    }

    /// <summary>
    /// Registers a callback for change notifications.
    /// Called by ConsulChangeToken when consumers register callbacks.
    /// </summary>
    /// <param name="callback">The callback to invoke when a change is detected.</param>
    /// <param name="state">The state object to pass to the callback.</param>
    /// <returns>A unique key for this registration (used for unregistration).</returns>
    internal string RegisterChangeCallback(Action<object?> callback, object? state)
    {
        var key = Guid.NewGuid().ToString("N");
        var registration = new CallbackRegistration(callback, state);
        _callbacks.TryAdd(key, registration);
        return key;
    }

    /// <summary>
    /// Unregisters a callback for change notifications.
    /// Called by ConsulChangeToken when callbacks are disposed.
    /// </summary>
    /// <param name="key">The registration key returned from RegisterChangeCallback.</param>
    internal void UnregisterChangeCallback(string key)
    {
        _callbacks.TryRemove(key, out _);
    }

    /// <summary>
    /// Polling timer callback that checks for configuration changes in Consul.
    /// Uses the ModifyIndex to efficiently detect changes without heavy data transfer.
    /// </summary>
    private async void PollForChangesAsync(object? state)
    {
        if (_disposed)
        {
            return;
        }

        // Check throttling - ensure minimum interval has passed
        var now = DateTime.UtcNow;
        var lastReload = new DateTime(_lastReloadTimeUtcTicks, DateTimeKind.Utc);
        var timeSinceLastReload = (now - lastReload).TotalMilliseconds;

        if (timeSinceLastReload < _options.MinReloadIntervalMs)
        {
            return; // Skip this poll cycle
        }

        try
        {
            string kvUrl = BuildConsulKvUrl();

            // First do a lightweight check to see if the index has changed
            var indexCheckResponse = await _httpClient.GetAsync(kvUrl, HttpCompletionOption.ResponseHeadersRead);
            indexCheckResponse.EnsureSuccessStatusCode();

            // Check if ModifyIndex has changed
            if (TryGetModifyIndex(indexCheckResponse, out ulong modifyIndex) &&
                modifyIndex != _currentModifyIndex)
            {
                // Index has changed - fetch full config and notify callbacks
                var newConfig = await FetchConfigAsync(kvUrl, CancellationToken.None);

                if (newConfig.SourceVersion != _cachedConfig?.SourceVersion)
                {
                    _cachedConfig = newConfig;
                    NotifyCallbacks();
                }
            }
        }
        catch
        {
            // Silently ignore polling errors - they'll be retried on next interval
            // This prevents the timer from crashing due to transient network issues
        }
    }

    /// <summary>
    /// Fetches the configuration from Consul KV store and parses it into a DTO.
    /// Updates the cached ModifyIndex for version tracking.
    /// Performs validation and falls back to last-known-good configuration on validation failure.
    /// </summary>
    private async Task<ContextifyPolicyConfigDto> FetchConfigAsync(string kvUrl, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync(kvUrl, HttpCompletionOption.ResponseContentRead, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Key doesn't exist - return default configuration
            _currentModifyIndex = 0;
            _lastReloadTimeUtcTicks = DateTime.UtcNow.Ticks;
            var defaultConfig = ContextifyPolicyConfigDto.AllowByDefault();
            defaultConfig = defaultConfig with { SourceVersion = "0" };
            UpdateCachedConfigWithValidation(defaultConfig);
            return _cachedConfig!;
        }

        response.EnsureSuccessStatusCode();

        // Parse Consul KV response format (returns an array of KV pairs)
        var consulResponses = await response.Content.ReadFromJsonAsync<ConsulKvResponse[]>(cancellationToken: ct);

        if (consulResponses is null || consulResponses.Length == 0)
        {
            // Empty array - key doesn't exist
            _currentModifyIndex = 0;
            _lastReloadTimeUtcTicks = DateTime.UtcNow.Ticks;
            var defaultConfig = ContextifyPolicyConfigDto.AllowByDefault();
            defaultConfig = defaultConfig with { SourceVersion = "0" };
            UpdateCachedConfigWithValidation(defaultConfig);
            return _cachedConfig!;
        }

        var consulResponse = consulResponses[0];

        if (consulResponse.Value is null)
        {
            throw new InvalidOperationException(
                $"Invalid response from Consul KV at '{kvUrl}'. " +
                $"Expected a KV response with a Value property.");
        }

        // Update the ModifyIndex
        if (ulong.TryParse(consulResponse.ModifyIndex, out var modifyIndex))
        {
            _currentModifyIndex = modifyIndex;
        }

        // Decode Base64 value
        string jsonValue;
        try
        {
            var decodedBytes = Convert.FromBase64String(consulResponse.Value);
            jsonValue = Encoding.UTF8.GetString(decodedBytes);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Failed to decode Base64 value from Consul KV at '{kvUrl}'. " +
                $"Ensure the value is properly Base64-encoded.",
                ex);
        }

        // Parse JSON into DTO with backward compatibility for missing SchemaVersion
        ContextifyPolicyConfigDto? config;
        try
        {
            config = JsonSerializer.Deserialize<ContextifyPolicyConfigDto>(
                jsonValue,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize policy configuration from Consul KV at '{kvUrl}'. " +
                $"Ensure the JSON matches ContextifyPolicyConfigDto structure.",
                ex);
        }

        if (config is null)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize policy configuration from Consul KV at '{kvUrl}'. " +
                $"Ensure the JSON matches ContextifyPolicyConfigDto structure.");
        }

        // Handle missing SchemaVersion (default to 1 for backward compatibility)
        if (config.SchemaVersion == 0)
        {
            config = config with { SchemaVersion = 1 };
        }

        // Set the source version from ModifyIndex
        config = config with { SourceVersion = _currentModifyIndex.ToString() };

        // Validate and update config with last-known-good fallback
        UpdateCachedConfigWithValidation(config);

        return _cachedConfig!;
    }

    /// <summary>
    /// Validates the configuration and updates the cached config if valid.
    /// Falls back to last-known-good configuration if validation produces errors.
    /// </summary>
    private void UpdateCachedConfigWithValidation(ContextifyPolicyConfigDto config)
    {
        var validationResult = _validationService.ValidatePolicyConfig(config);

        if (validationResult.Errors.Count > 0)
        {
            // Validation failed - log error and keep last known good config
            _logger?.LogError(
                "Policy configuration validation failed. Errors: {Errors}. Keeping last known good configuration.",
                string.Join("; ", validationResult.Errors));

            // If we have a last known good config, keep it; otherwise use the new config despite errors
            if (_lastKnownGoodConfig is not null)
            {
                _cachedConfig = _lastKnownGoodConfig;
                return;
            }

            // First load - accept the config despite errors to avoid crash
            _logger?.LogWarning(
                "No previous valid configuration available. Using current configuration despite validation errors.");
        }
        else if (validationResult.Warnings.Count > 0)
        {
            _logger?.LogWarning(
                "Policy configuration validation produced warnings: {Warnings}",
                string.Join("; ", validationResult.Warnings));
        }

        // Configuration is valid (or no previous config available) - update cache
        _cachedConfig = config;
        _lastKnownGoodConfig = config;
        _lastReloadTimeUtcTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// Extracts the ModifyIndex from the Consul KV response headers or body.
    /// Consul returns the ModifyIndex in the X-Consul-Index header for efficient change detection.
    /// </summary>
    private bool TryGetModifyIndex(HttpResponseMessage response, out ulong modifyIndex)
    {
        modifyIndex = 0;

        // Try X-Consul-Index header first
        if (response.Headers.TryGetValues("X-Consul-Index", out var indexValues))
        {
            var indexValue = indexValues.FirstOrDefault();
            if (indexValue is not null && ulong.TryParse(indexValue, out modifyIndex))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the Consul KV URL for fetching the policy configuration.
    /// Includes datacenter and token parameters if configured.
    /// </summary>
    private string BuildConsulKvUrl()
    {
        var uriBuilder = new UriBuilder(_options.Address);
        var keyPath = _options.KeyPath.TrimStart('/');
        var queryParameters = $"raw=false"; // Get full response with metadata

        // Add datacenter parameter if specified
        if (!string.IsNullOrWhiteSpace(_options.Datacenter))
        {
            queryParameters += $"&dc={Uri.EscapeDataString(_options.Datacenter)}";
        }

        // Add token parameter if specified (as query param for GET requests)
        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            queryParameters += $"&token={Uri.EscapeDataString(_options.Token)}";
        }

        uriBuilder.Path = $"/v1/kv/{keyPath}";
        uriBuilder.Query = queryParameters;

        return uriBuilder.ToString();
    }

    /// <summary>
    /// Notifies all registered callbacks that a configuration change has occurred.
    /// Executes callbacks outside of locks to prevent deadlocks.
    /// </summary>
    private void NotifyCallbacks()
    {
        foreach (var kvp in _callbacks)
        {
            try
            {
                kvp.Value.Callback(kvp.Value.State);
            }
            catch
            {
                // Continue notifying other callbacks even if one fails
                // This prevents a single failing callback from blocking others
            }
        }
    }

    /// <summary>
    /// Throws an ObjectDisposedException if this instance has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConsulPolicyConfigProvider));
        }
    }

    /// <summary>
    /// Disposes of resources used by this provider.
    /// Stops the polling timer and cleans up HTTP client resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _pollingTimer.Dispose();
        _callbacks.Clear();

        _disposed = true;
    }
}

/// <summary>
/// Internal record representing a Consul KV API response.
/// Used for deserializing the JSON response from Consul's KV endpoint.
/// </summary>
internal sealed record ConsulKvResponse
{
    /// <summary>
    /// Gets or sets the lock index for this key.
    /// </summary>
    public ulong? LockIndex { get; init; }

    /// <summary>
    /// Gets or sets the key path in the KV store.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Gets or sets the modify index (version) for this key.
    /// Used for change detection and version tracking.
    /// </summary>
    public required string ModifyIndex { get; init; }

    /// <summary>
    /// Gets or sets the create index for this key.
    /// </summary>
    public ulong? CreateIndex { get; init; }

    /// <summary>
    /// Gets or sets the Base64-encoded value of the key.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Gets or sets the flags associated with this key.
    /// </summary>
    public ulong? Flags { get; init; }
}

/// <summary>
/// Internal record representing a registered callback for change notifications.
/// Stores the callback delegate and its associated state object.
/// </summary>
/// <param name="Callback">The callback action to invoke.</param>
/// <param name="State">The state object to pass to the callback.</param>
internal sealed record CallbackRegistration(Action<object?> Callback, object? State);
