using Contextify.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Contextify.Core.Logging;

/// <summary>
/// Resolves the appropriate log sink based on available services.
/// Implements a fallback chain:
/// 1. IContextifyLogging if registered
/// 2. ILogger/ILoggerFactory if available
/// 3. Console sink as final fallback
/// </summary>
internal static class ContextifyLogSinkResolver
{
    /// <summary>
    /// Resolves the appropriate log sink from the service provider.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve services from.</param>
    /// <param name="minimumLevel">The minimum log level for the sink (used for console fallback).</param>
    /// <returns>A resolved log sink instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when serviceProvider is null.</exception>
    public static IContextifyLogSink Resolve(
        IServiceProvider serviceProvider,
        ContextifyLogLevel minimumLevel = ContextifyLogLevel.Information)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        // First, try to resolve IContextifyLogging
        var contextifyLogging = serviceProvider.GetService<IContextifyLogging>();
        if (contextifyLogging is not null)
        {
            return new ContextifyLoggingSink(contextifyLogging);
        }

        // Second, try to resolve ILoggerFactory
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        if (loggerFactory is not null)
        {
            var loggerFromFactory = loggerFactory.CreateLogger("Contextify");
            return new ILoggerLogSink(loggerFromFactory, minimumLevel);
        }

        // Third, try to resolve ILogger directly
        var logger = serviceProvider.GetService<ILogger>();
        if (logger is not null)
        {
            return new ILoggerLogSink(logger, minimumLevel);
        }

        // Final fallback: Console sink (no DI required)
        return new ConsoleLogSink(minimumLevel);
    }

    /// <summary>
    /// Resolves the appropriate log sink from the service provider asynchronously.
    /// This is a synchronous operation but follows the async pattern for consistency.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve services from.</param>
    /// <param name="minimumLevel">The minimum log level for the sink.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task representing the resolved log sink instance.</returns>
    public static Task<IContextifyLogSink> ResolveAsync(
        IServiceProvider serviceProvider,
        ContextifyLogLevel minimumLevel = ContextifyLogLevel.Information,
        CancellationToken cancellationToken = default)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IContextifyLogSink>(cancellationToken);
        }

        var sink = Resolve(serviceProvider, minimumLevel);
        return Task.FromResult(sink);
    }

    /// <summary>
    /// Checks if IContextifyLogging is registered in the service provider.
    /// </summary>
    /// <param name="serviceProvider">The service provider to check.</param>
    /// <returns>True if IContextifyLogging is registered; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when serviceProvider is null.</exception>
    public static bool HasContextifyLogging(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        return serviceProvider.GetService<IContextifyLogging>() is not null;
    }

    /// <summary>
    /// Checks if ILoggerFactory is registered in the service provider.
    /// </summary>
    /// <param name="serviceProvider">The service provider to check.</param>
    /// <returns>True if ILoggerFactory is registered; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when serviceProvider is null.</exception>
    public static bool HasLoggerFactory(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        return serviceProvider.GetService<ILoggerFactory>() is not null;
    }

    /// <summary>
    /// Checks if ILogger is registered in the service provider.
    /// </summary>
    /// <param name="serviceProvider">The service provider to check.</param>
    /// <returns>True if ILogger is registered; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when serviceProvider is null.</exception>
    public static bool HasLogger(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        return serviceProvider.GetService<ILogger>() is not null;
    }
}
