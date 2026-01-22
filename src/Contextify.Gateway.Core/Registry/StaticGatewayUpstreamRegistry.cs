using Contextify.Gateway.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Contextify.Gateway.Core.Registry;

/// <summary>
/// Static registry that reads upstream configurations from gateway options.
/// Provides upstream configurations from IOptionsMonitor without runtime modification.
/// Returns only enabled upstreams, allowing runtime disabling without configuration changes.
/// Thread-safe for concurrent read operations using options monitor snapshot pattern.
/// </summary>
public sealed class StaticGatewayUpstreamRegistry : IContextifyGatewayUpstreamRegistry
{
    private readonly IOptionsMonitor<ContextifyGatewayOptionsEntity> _optionsMonitor;

    /// <summary>
    /// Gets the options monitor used to retrieve current gateway configuration.
    /// Provides access to the latest configuration without requiring service restart.
    /// </summary>
    public IOptionsMonitor<ContextifyGatewayOptionsEntity> OptionsMonitor => _optionsMonitor;

    /// <summary>
    /// Initializes a new instance with options monitor for configuration access.
    /// Uses IOptionsMonitor to support configuration reload without application restart.
    /// </summary>
    /// <param name="optionsMonitor">The options monitor for gateway configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when optionsMonitor is null.</exception>
    public StaticGatewayUpstreamRegistry(IOptionsMonitor<ContextifyGatewayOptionsEntity> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
    }

    /// <summary>
    /// Retrieves all enabled upstream configurations from the current gateway options.
    /// Filters out disabled upstreams to return only active service endpoints.
    /// Returns a snapshot that remains consistent even if configuration is reloaded.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
    /// <returns>A task yielding a read-only list of enabled upstream configuration entities.</returns>
    public ValueTask<IReadOnlyList<ContextifyGatewayUpstreamEntity>> GetUpstreamsAsync(CancellationToken cancellationToken)
    {
        // Check for cancellation
        cancellationToken.ThrowIfCancellationRequested();

        // Get current options snapshot
        var currentOptions = _optionsMonitor.CurrentValue;

        // Filter and return enabled upstreams
        var enabledUpstreams = currentOptions.GetEnabledUpstreams();

        return ValueTask.FromResult(enabledUpstreams);
    }
}
