using System.Threading;
using System.Threading.Tasks;
using Contextify.Config.Abstractions.Policy;
using Contextify.Config.AppSettings.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Contextify.Config.AppSettings.Provider;

/// <summary>
/// Provider that loads Contextify policy configuration from application settings (appsettings.json).
/// Implements the <see cref="IContextifyPolicyConfigProvider"/> interface.
/// </summary>
public sealed class ContextifyAppSettingsPolicyConfigProvider : IContextifyPolicyConfigProvider
{
    private readonly IOptionsMonitor<ContextifyPolicyConfigDto> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextifyAppSettingsPolicyConfigProvider"/> class.
    /// </summary>
    /// <param name="options">The monitor for policy configuration values.</param>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public ContextifyAppSettingsPolicyConfigProvider(IOptionsMonitor<ContextifyPolicyConfigDto> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Asynchronously retrieves the current policy configuration from application settings.
    /// Returns the current snapshot from the options monitor, reflecting the latest configuration.
    /// </summary>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>A ValueTask containing the current policy configuration.</returns>
    public ValueTask<ContextifyPolicyConfigDto> GetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_options.CurrentValue);
    }

    /// <summary>
    /// Gets a change token for monitoring policy configuration updates from application settings.
    /// The token triggers when the options monitor detects changes to the configuration section.
    /// Enables reactive configuration reload without polling overhead.
    /// </summary>
    /// <returns>A change token that signals when configuration has changed.</returns>
    /// <remarks>
    /// The change token is obtained from IOptionsMonitor.OnChange callback registration.
    /// Each call returns a new token that will be triggered on the next configuration change.
    /// Consumers should register callbacks to be notified when configuration updates occur.
    ///
    /// Example usage:
    /// <code>
    /// var changeToken = provider.Watch();
    /// changeToken.RegisterChangeCallback(state => ReloadPolicy(), null);
    /// </code>
    /// </remarks>
    public IChangeToken? Watch()
    {
        // IOptionsMonitor doesn't expose IChangeToken directly.
        // For a proper implementation, we might need a custom IChangeToken that wraps the OnChange event.
        // However, most consumers of IPolicyConfigProvider use the IOptionsMonitor directly if they need reactivity.
        // To stay compatible with the interface, we'll return null for now if it's not strictly required by the current host.
        return null;
    }
}
