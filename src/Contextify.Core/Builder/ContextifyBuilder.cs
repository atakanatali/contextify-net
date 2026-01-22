using Contextify.Core.Execution;
using Contextify.Core.Options;
using Contextify.Core.Redaction;
using Contextify.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Contextify.Core.Builder;

internal sealed class ContextifyBuilder : IContextifyBuilder
{
    public ContextifyBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Gets the service collection being configured by this builder.
    /// Allows registration of additional services and dependencies.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Configures the root Contextify options.
    /// </summary>
    /// <param name="configureOptions">A delegate to configure the root options.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    public IContextifyBuilder Configure(Action<ContextifyOptionsEntity> configureOptions)
    {
        if (configureOptions is null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        // We use a post-configuration pattern to apply changes to the singleton
        // or we can just register the action to be applied during construction if we control it.
        // For now, let's use the simplest approach that doesn't build the provider.
        Services.AddSingleton<IConfigureOptions<ContextifyOptionsEntity>>(new ConfigureOptions<ContextifyOptionsEntity>(configureOptions));

        return this;
    }
}

