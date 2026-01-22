using Contextify.Core.Options;
using Contextify.Core.Redaction;
using Contextify.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Contextify.Core.Builder;

/// <summary>
/// Defines a fluent builder interface for configuring Contextify services.
/// Provides methods to configure logging, policy, actions, and other framework options.
/// Supports method chaining for a fluent configuration API.
/// </summary>
public interface IContextifyBuilder
{
    /// <summary>
    /// Gets the service collection being configured by this builder.
    /// Allows registration of additional services and dependencies.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Configures the root Contextify options.
    /// </summary>
    /// <param name="configureOptions">A delegate to configure the root options.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    IContextifyBuilder Configure(Action<ContextifyOptionsEntity> configureOptions);
}

