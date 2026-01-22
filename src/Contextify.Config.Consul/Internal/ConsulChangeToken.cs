using Microsoft.Extensions.Primitives;
using Contextify.Config.Consul.Provider;

namespace Contextify.Config.Consul.Internal;

/// <summary>
/// Change token implementation for Consul-based configuration change detection.
/// Provides IChangeToken semantics for polling-based configuration updates from Consul.
/// Tracks callback registrations and supports callback disposal on token disposal.
/// Thread-safe implementation allowing multiple concurrent callback registrations.
/// </summary>
internal sealed class ConsulChangeToken : IChangeToken
{
    private readonly ConsulPolicyConfigProvider _provider;
    private readonly string _registrationKey;
    private int _hasChanged;
    private bool _activeChangeCallbacks;

    /// <summary>
    /// Initializes a new instance with the associated provider.
    /// Registers this token with the provider for change notifications.
    /// </summary>
    /// <param name="provider">The Consul policy config provider that owns this token.</param>
    /// <exception cref="ArgumentNullException">Thrown when provider is null.</exception>
    public ConsulChangeToken(ConsulPolicyConfigProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _registrationKey = _provider.RegisterChangeCallback(OnChangeNotified, null);
        _hasChanged = 0;
        _activeChangeCallbacks = true;
    }

    /// <summary>
    /// Gets a value indicating whether a change has occurred.
    /// True after the provider notifies this token of a configuration change.
    /// Resets to false after the change has been processed.
    /// </summary>
    public bool HasChanged => Volatile.Read(ref _hasChanged) == 1;

    /// <summary>
    /// Gets a value indicating whether this token will actively raise callbacks.
    /// Always returns true for Consul change tokens as they support proactive change notifications.
    /// </summary>
    public bool ActiveChangeCallbacks => _activeChangeCallbacks;

    /// <summary>
    /// Registers a callback to be invoked when a change is detected.
    /// The callback is executed on the polling thread when a configuration change is detected.
    /// </summary>
    /// <param name="callback">The callback to invoke when changes occur.</param>
    /// <param name="state">The state object to pass to the callback.</param>
    /// <returns>An IDisposable that unregisters the callback when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when callback is null.</exception>
    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        return new ConsulChangeTokenRegistration(_provider, callback, state);
    }

    /// <summary>
    /// Internal callback invoked by the provider when a configuration change is detected.
    /// Sets HasChanged to true and allows consumers to detect the change.
    /// </summary>
    private void OnChangeNotified(object? state)
    {
        Interlocked.Exchange(ref _hasChanged, 1);
    }

    /// <summary>
    /// Disposes this change token and unregisters it from the provider.
    /// Prevents further change notifications to this token.
    /// </summary>
    public void Dispose()
    {
        _activeChangeCallbacks = false;
        _provider.UnregisterChangeCallback(_registrationKey);
    }
}

/// <summary>
/// Registration for a change callback on a Consul change token.
/// Implements IDisposable to allow unregistration of the callback.
/// </summary>
internal sealed class ConsulChangeTokenRegistration : IDisposable
{
    private readonly Action<object?> _callback;
    private readonly object? _state;
    private readonly ConsulPolicyConfigProvider _provider;
    private readonly string _callbackKey;
    private bool _disposed;
    private int _callbackExecuted;

    /// <summary>
    /// Initializes a new instance and registers the callback with the provider.
    /// The callback will be invoked when a configuration change is detected.
    /// </summary>
    /// <param name="provider">The Consul policy config provider.</param>
    /// <param name="callback">The callback to invoke on change.</param>
    /// <param name="state">The state object to pass to the callback.</param>
    public ConsulChangeTokenRegistration(
        ConsulPolicyConfigProvider provider,
        Action<object?> callback,
        object? state)
    {
        _provider = provider;
        _callback = callback;
        _state = state;
        _callbackKey = _provider.RegisterChangeCallback(OnProviderChange, null);
    }

    /// <summary>
    /// Callback invoked by the provider when a change is detected.
    /// Executes the user-provided callback with the associated state.
    /// Ensures the callback is only executed once to prevent duplicate notifications.
    /// </summary>
    private void OnProviderChange(object? state)
    {
        // Only execute if we haven't executed before (prevent duplicate callbacks)
        if (Interlocked.CompareExchange(ref _callbackExecuted, 1, 0) == 0)
        {
            try
            {
                _callback(_state);
            }
            catch
            {
                // Swallow callback exceptions to prevent provider failure
            }
        }
    }

    /// <summary>
    /// Disposes this registration and unregisters the callback from the provider.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _provider.UnregisterChangeCallback(_callbackKey);
        _disposed = true;
    }
}
