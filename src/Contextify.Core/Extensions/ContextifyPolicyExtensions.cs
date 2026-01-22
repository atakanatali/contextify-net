using System;
using Contextify.Core.Builder;
using Contextify.Config.Abstractions.Builder;

namespace Contextify.Core.Extensions;

/// <summary>
/// Extension methods for IContextifyBuilder to configure policy.
/// </summary>
public static class ContextifyPolicyExtensions
{
    /// <summary>
    /// Configures policy for the Contextify application.
    /// </summary>
    /// <param name="builder">The Contextify builder.</param>
    /// <param name="configure">Delegate to configure the policy builder.</param>
    /// <returns>The Contextify builder for chaining.</returns>
    public static IContextifyBuilder ConfigurePolicy(
        this IContextifyBuilder builder,
        Action<ContextifyPolicyBuilder> configure)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var policyBuilder = new ContextifyPolicyBuilder(builder.Services);
        configure(policyBuilder);

        return builder;
    }
}
