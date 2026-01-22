using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contextify.Gateway.Core.RateLimit;

/// <summary>
/// Background service that periodically cleans up stale rate limiter entries from the cache.
/// Removes entries that haven't been accessed within the expiration period to control memory usage.
/// Runs on a timer based on the configured cleanup interval.
/// </summary>
public sealed class ContextifyGatewayRateLimitCleanupService : BackgroundService
{
    private readonly ILogger<ContextifyGatewayRateLimitCleanupService> _logger;
    private readonly ContextifyGatewayRateLimitOptionsEntity _options;
    private readonly ContextifyGatewayRateLimiterCache _limiterCache;
    private readonly PeriodicTimer _timer;

    /// <summary>
    /// Initializes a new instance of the rate limit cleanup service.
    /// </summary>
    /// <param name="logger">Logger for diagnostics and tracing.</param>
    /// <param name="options">Rate limiting configuration options.</param>
    /// <param name="limiterCache">The rate limiter cache to clean up.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyGatewayRateLimitCleanupService(
        ILogger<ContextifyGatewayRateLimitCleanupService> logger,
        IOptions<ContextifyGatewayRateLimitOptionsEntity> options,
        ContextifyGatewayRateLimiterCache limiterCache)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var optionsValue = options?.Value
            ?? throw new ArgumentNullException(nameof(options));

        _options = optionsValue;
        _limiterCache = limiterCache ?? throw new ArgumentNullException(nameof(limiterCache));
        _timer = new PeriodicTimer(_options.CleanupInterval);
    }

    /// <summary>
    /// Executes the background cleanup loop.
    /// Periodically removes stale rate limiter entries from the cache.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for stopping the service.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Rate limit cleanup service started. CleanupInterval: {CleanupInterval}, EntryExpiration: {EntryExpiration}",
            _options.CleanupInterval,
            _options.EntryExpiration);

        while (!stoppingToken.IsCancellationRequested && await _timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                PerformCleanup();
            }
            catch (OperationCanceledException)
            {
                // Service is shutting down
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during rate limit cache cleanup");
            }
        }

        _logger.LogInformation("Rate limit cleanup service stopped");
    }

    /// <summary>
    /// Performs the cleanup operation to remove stale rate limiter entries.
    /// </summary>
    private void PerformCleanup()
    {
        var countBefore = _limiterCache.Count;

        if (countBefore == 0)
        {
            _logger.LogDebug("Rate limit cache is empty, skipping cleanup");
            return;
        }

        var removed = _limiterCache.RemoveStaleEntries();
        var countAfter = _limiterCache.Count;

        if (removed > 0)
        {
            _logger.LogDebug(
                "Rate limit cache cleanup completed. Removed: {Removed} entries, Before: {Before}, After: {After}",
                removed,
                countBefore,
                countAfter);
        }
        else
        {
            _logger.LogDebug(
                "Rate limit cache cleanup completed. No stale entries found. Current count: {Count}",
                countAfter);
        }
    }

    /// <summary>
    /// Disposes the periodic timer when the service is stopped.
    /// </summary>
    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }
}
