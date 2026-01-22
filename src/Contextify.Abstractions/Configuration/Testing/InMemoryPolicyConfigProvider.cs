using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;

namespace Contextify.Config.Abstractions.Policy;

/// <summary>
/// In-memory implementation of IContextifyPolicyConfigProvider for testing scenarios.
/// Provides a simple policy configuration source that returns a fixed configuration.
/// Change tokens are not supported (returns null).
/// </summary>
public sealed class InMemoryPolicyConfigProvider : IContextifyPolicyConfigProvider
{
    /// <summary>
    /// Gets the policy configuration provided by this instance.
    /// </summary>
    private readonly ContextifyPolicyConfigDto _config;

    /// <summary>
    /// Initializes a new instance with the specified policy configuration.
    /// </summary>
    /// <param name="config">The policy configuration to provide.</param>
    /// <exception cref="ArgumentNullException">Thrown when config is null.</exception>
    public InMemoryPolicyConfigProvider(ContextifyPolicyConfigDto config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Asynchronously retrieves the current policy configuration.
    /// Returns the fixed configuration provided at construction time.
    /// </summary>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>A ValueTask containing the current policy configuration.</returns>
    public ValueTask<ContextifyPolicyConfigDto> GetAsync(CancellationToken ct)
    {
        return ValueTask.FromResult(_config);
    }

    /// <summary>
    /// Gets a change token for monitoring policy configuration updates.
    /// Returns null as in-memory provider does not support change notifications.
    /// </summary>
    /// <returns>Always returns null.</returns>
    public IChangeToken? Watch()
    {
        return null;
    }
}
