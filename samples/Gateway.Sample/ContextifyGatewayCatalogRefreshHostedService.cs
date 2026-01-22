using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contextify.Gateway.Host;

/// <summary>
/// Background service that periodically refreshes the gateway tool catalog.
/// Calls EnsureFreshSnapshotAsync on the catalog aggregator at configured intervals.
/// Uses a semaphore to prevent overlapping refresh operations in high-load scenarios.
/// Designed for enterprise-scale deployments with millions of concurrent requests.
/// </summary>
public sealed class ContextifyGatewayCatalogRefreshHostedService : BackgroundService
{
    private readonly ContextifyGatewayCatalogAggregatorService _catalogAggregator;
    private readonly IOptionsMonitor<ContextifyGatewayOptionsEntity> _optionsMonitor;
    private readonly ILogger<ContextifyGatewayCatalogRefreshHostedService> _logger;
    private readonly SemaphoreSlim _refreshLock;

    /// <summary>
    /// Gets the semaphore used to prevent overlapping refresh operations.
    /// Ensures that only one refresh can be active at a time, even if the previous run takes longer than the interval.
    /// </summary>
    public SemaphoreSlim RefreshLock => _refreshLock;

    /// <summary>
    /// Initializes a new instance of the catalog refresh hosted service.
    /// Sets up dependencies for periodic catalog refresh operations.
    /// </summary>
    /// <param name="catalogAggregator">The catalog aggregator service to refresh.</param>
    /// <param name="optionsMonitor">The options monitor for gateway configuration.</param>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyGatewayCatalogRefreshHostedService(
        ContextifyGatewayCatalogAggregatorService catalogAggregator,
        IOptionsMonitor<ContextifyGatewayOptionsEntity> optionsMonitor,
        ILogger<ContextifyGatewayCatalogRefreshHostedService> logger)
    {
        _catalogAggregator = catalogAggregator ?? throw new ArgumentNullException(nameof(catalogAggregator));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create a semaphore with max count of 1 to prevent overlapping refreshes
        _refreshLock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Executes the background service loop.
    /// Periodically triggers catalog refresh operations at the configured interval.
    /// Uses the semaphore to ensure only one refresh operation runs at a time.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token to stop the service.</param>
    /// <returns>A task representing the async operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Gateway catalog refresh hosted service starting");

        try
        {
            // Perform an initial refresh on startup
            await PerformRefreshAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var refreshInterval = _optionsMonitor.CurrentValue.CatalogRefreshInterval;

                _logger.LogDebug(
                    "Scheduling next catalog refresh in {Interval} seconds",
                    refreshInterval.TotalSeconds);

                // Wait for the configured interval or stop signal
                await Task.Delay(refreshInterval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                // Perform the refresh (will skip if already in progress)
                _ = PerformRefreshAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            _logger.LogInformation("Gateway catalog refresh hosted service stopping");
        }
    }

    /// <summary>
    /// Performs a catalog refresh operation if one is not already in progress.
    /// Uses the semaphore to check and acquire the refresh lock before proceeding.
    /// Logs the outcome of the refresh operation for monitoring and diagnostics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the async operation.</returns>
    private async Task PerformRefreshAsync(CancellationToken cancellationToken)
    {
        // Check if a refresh is already in progress
        if (!_refreshLock.Wait(0))
        {
            _logger.LogDebug(
                "Catalog refresh already in progress, skipping this cycle");
            return;
        }

        try
        {
            _logger.LogInformation("Starting periodic catalog refresh");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Call EnsureFreshSnapshotAsync which will only rebuild if needed
            var snapshot = await _catalogAggregator.EnsureFreshSnapshotAsync(cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Periodic catalog refresh completed in {ElapsedMs}ms. " +
                "Tools: {ToolCount}, Upstreams: {TotalUpstreams}/{HealthyUpstreams} healthy, SnapshotAge: {SnapshotAge}s",
                stopwatch.ElapsedMilliseconds,
                snapshot.ToolCount,
                snapshot.UpstreamCount,
                snapshot.HealthyUpstreamCount,
                (DateTime.UtcNow - snapshot.CreatedUtc).TotalSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Catalog refresh canceled due to service shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error during periodic catalog refresh");
        }
        finally
        {
            // Always release the lock
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Disposes of resources used by the hosted service.
    /// Ensures the semaphore is properly disposed during shutdown.
    /// </summary>
    public override void Dispose()
    {
        _refreshLock?.Dispose();
        base.Dispose();
    }
}
