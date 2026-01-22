using Microsoft.Extensions.DependencyInjection;

namespace Contextify.Config.Abstractions.Builder;

/// <summary>
/// Builder for configuring the policy provider source.
/// Provides methods to specify where policy configuration should be loaded from.
/// </summary>
public sealed class ContextifyPolicyBuilder
{
    /// <summary>
    /// Gets the service collection to register services with.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Initializes a new instance with the service collection to configure.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    public ContextifyPolicyBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
