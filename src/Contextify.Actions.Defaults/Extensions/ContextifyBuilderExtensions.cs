using System;
using Contextify.Actions.Abstractions;
using Contextify.Actions.Defaults.Actions;
using Contextify.Core.Builder;
using Contextify.Core.Redaction;
using Microsoft.Extensions.DependencyInjection;

namespace Contextify.Actions.Defaults.Extensions;

/// <summary>
/// Extension methods for the Contextify builder to register default actions.
/// Provides fluent API for adding timeout, concurrency, and rate limiting actions.
/// </summary>
public static class ContextifyBuilderExtensions
{
    /// <summary>
    /// Adds all default actions to the Contextify builder.
    /// Registers TimeoutAction, ConcurrencyAction, RateLimitAction, and OutputRedactionAction with DI.
    /// Actions are automatically discovered and applied based on effective policy configuration.
    /// </summary>
    /// <param name="builder">The Contextify builder instance.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    /// <remarks>
    /// The following actions are registered:
    /// - TimeoutAction (Order 100): Enforces timeout limits based on policy.TimeoutMs
    /// - ConcurrencyAction (Order 110): Enforces concurrency limits based on policy.ConcurrencyLimit
    /// - RateLimitAction (Order 120): Enforces rate limits based on policy.RateLimitPolicy
    /// - OutputRedactionAction (Order 200): Redacts sensitive information from tool output
    ///
    /// Each action only applies when the corresponding policy is configured for a tool.
    /// Redaction action always applies but the redaction service handles fast-path returns when disabled.
    /// </remarks>
    public static IContextifyBuilder AddDefaults(this IContextifyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        // Register all default actions as singleton services
        // Singleton is appropriate because actions are stateless (except for cached rate limiters)
        builder.Services.AddSingleton<IContextifyAction, TimeoutAction>();
        builder.Services.AddSingleton<IContextifyAction, ConcurrencyAction>();
        builder.Services.AddSingleton<IContextifyAction, RateLimitAction>();
        builder.Services.AddSingleton<IContextifyAction, OutputRedactionAction>();

        return builder;
    }

    /// <summary>
    /// Adds the timeout action to the Contextify builder.
    /// Registers only the TimeoutAction for timeout enforcement.
    /// </summary>
    /// <param name="builder">The Contextify builder instance.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    public static IContextifyBuilder AddTimeoutAction(this IContextifyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.AddSingleton<IContextifyAction, TimeoutAction>();
        return builder;
    }

    /// <summary>
    /// Adds the concurrency action to the Contextify builder.
    /// Registers only the ConcurrencyAction for concurrency limit enforcement.
    /// </summary>
    /// <param name="builder">The Contextify builder instance.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    public static IContextifyBuilder AddConcurrencyAction(this IContextifyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.AddSingleton<IContextifyAction, ConcurrencyAction>();
        return builder;
    }

    /// <summary>
    /// Adds the rate limit action to the Contextify builder.
    /// Registers only the RateLimitAction for rate limit enforcement.
    /// </summary>
    /// <param name="builder">The Contextify builder instance.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    public static IContextifyBuilder AddRateLimitAction(this IContextifyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.AddSingleton<IContextifyAction, RateLimitAction>();
        return builder;
    }

    /// <summary>
    /// Adds the output redaction action to the Contextify builder.
    /// Registers only the OutputRedactionAction for sanitizing tool output.
    /// The redaction service handles fast-path returns when redaction is disabled.
    /// </summary>
    /// <param name="builder">The Contextify builder instance.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    /// <remarks>
    /// The OutputRedactionAction applies after tool execution (Order 200) to sanitize output.
    /// Redaction uses field-name based JSON redaction and optional pattern-based text redaction.
    /// Configure redaction via builder.ConfigureRedaction() to enable and specify fields/patterns.
    /// </remarks>
    public static IContextifyBuilder AddOutputRedactionAction(this IContextifyBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.AddSingleton<IContextifyAction, OutputRedactionAction>();
        return builder;
    }
}
