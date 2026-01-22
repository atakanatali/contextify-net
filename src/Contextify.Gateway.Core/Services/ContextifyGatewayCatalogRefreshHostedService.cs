using System;
using System.Threading;
using System.Threading.Tasks;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contextify.Gateway.Core.Services;

/// <summary>
/// Hosted service that periodically refreshes the gateway tool catalog.
/// Ensures that the catalog snapshot is kept up-to-date with upstream changes
/// independently of incoming requests, reducing latency for the first request
/// after a cache expiration.
/// </summary>
public sealed class ContextifyGatewayCatalogRefreshHostedService : BackgroundService
{
    private readonly ContextifyGatewayCatalogAggregatorService _catalogAggregator;
    private readonly ContextifyGatewayOptionsEntity _options;
    private readonly ILogger<ContextifyGatewayCatalogRefreshHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayCatalogRefreshHostedService class.
    /// </summary>
    /// <param name="catalogAggregator">The catalog aggregator service to refresh.</param>
    /// <param name="options">The gateway configuration options.</param>
    /// <param name="logger">The logger for diagnostic messages.</param>
    public ContextifyGatewayCatalogRefreshHostedService(
        ContextifyGatewayCatalogAggregatorService catalogAggregator,
        IOptions<ContextifyGatewayOptionsEntity> options,
        ILogger<ContextifyGatewayCatalogRefreshHostedService> logger)
    {
        _catalogAggregator = catalogAggregator ?? throw new ArgumentNullException(nameof(catalogAggregator));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Contextify Gateway Catalog Refresh Service starting. Interval: {Interval}", _options.CatalogRefreshInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Refresh the catalog snapshot
                await _catalogAggregator.EnsureFreshSnapshotAsync(stoppingToken);
                
                _logger.LogDebug("Gateway catalog refreshed successfully.");
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while refreshing gateway catalog.");
            }

            // Wait for the configured interval before next refresh
            try
            {
                await Task.Delay(_options.CatalogRefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown during delay
                break;
            }
        }

        _logger.LogInformation("Contextify Gateway Catalog Refresh Service stopped.");
    }
}
