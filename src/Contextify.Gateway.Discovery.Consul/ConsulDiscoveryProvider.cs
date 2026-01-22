using System.Diagnostics;
using System.Text.Json;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.Discovery;
using Contextify.Gateway.Discovery.Consul.Configuration;
using Contextify.Gateway.Discovery.Consul.Models;
using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Contextify.Gateway.Discovery.Consul;

/// <summary>
/// Consul-based discovery provider for dynamically finding upstream MCP servers.
/// Queries the Consul catalog to discover services and fetches their Contextify manifests.
/// Supports filtering by service tags and name prefixes for targeted discovery.
/// Implements change notification via polling for seamless upstream registration updates.
/// </summary>
public sealed class ConsulDiscoveryProvider : IContextifyGatewayDiscoveryProvider, IDisposable
{
    private readonly ContextifyGatewayConsulDiscoveryOptionsEntity _options;
    private readonly ILogger<ConsulDiscoveryProvider> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConsulClient _consulClient;
    private readonly CancellationChangeTokenSource _changeTokenSource;
    private readonly SemaphoreSlim _discoveryLock;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private bool _disposed;

    /// <summary>
    /// Gets the Consul client used for catalog and health queries.
    /// Provides access to service discovery and health check information.
    /// </summary>
    public ConsulClient ConsulClient => _consulClient;

    /// <summary>
    /// Gets the cancellation token source for change notifications.
    /// Used to signal when discovered upstreams have changed.
    /// </summary>
    internal CancellationChangeTokenSource ChangeTokenSource => _changeTokenSource;

    /// <summary>
    /// Initializes a new instance of the Consul discovery provider.
    /// Sets up Consul client connection and HTTP client for manifest fetching.
    /// </summary>
    /// <param name="options">The Consul discovery configuration options.</param>
    /// <param name="logger">The logger for diagnostics and tracing.</param>
    /// <param name="httpClientFactory">The HTTP client factory for manifest requests.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ConsulDiscoveryProvider(
        IOptions<ContextifyGatewayConsulDiscoveryOptionsEntity> options,
        ILogger<ConsulDiscoveryProvider> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

        // Validate options
        _options.Validate();

        // Create Consul client
        var consulConfig = new ConsulClientConfiguration
        {
            Address = _options.Address,
            Token = _options.Token,
            Datacenter = _options.Datacenter,
            WaitTime = TimeSpan.FromMinutes(5) // Long polling for blocking queries
        };

        _consulClient = new ConsulClient(consulConfig);

        // Initialize change notification
        _changeTokenSource = new CancellationChangeTokenSource();
        _discoveryLock = new SemaphoreSlim(1, 1);

        // Configure JSON serializer for manifest parsing
        _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        _logger.LogInformation(
            "Consul discovery provider initialized. Address: {Address}, TagFilter: {TagFilter}, PrefixFilter: {PrefixFilter}",
            _options.Address,
            _options.ServiceTagFilter ?? "(none)",
            _options.ServiceNamePrefix ?? "(none)");
    }

    /// <summary>
    /// Discovers all available upstream MCP servers from Consul.
    /// Queries the service catalog, applies filters, and fetches manifests from discovered services.
    /// Returns a snapshot of discovered upstream configurations at the time of the call.
    /// Thread-safe implementation using semaphore to prevent concurrent discovery operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the asynchronous operation.</param>
    /// <returns>A task yielding a read-only list of discovered upstream configuration entities.</returns>
    public async ValueTask<IReadOnlyList<ContextifyGatewayUpstreamEntity>> DiscoverAsync(CancellationToken cancellationToken)
    {
        // Prevent concurrent discovery operations
        await _discoveryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _logger.LogDebug("Starting Consul upstream discovery");

            var stopwatch = Stopwatch.StartNew();
            var discoveredUpstreams = new List<ContextifyGatewayUpstreamEntity>();

            // Query Consul catalog for services matching filters
            var allServices = await QueryConsulServicesAsync(cancellationToken).ConfigureAwait(false);

            if (allServices.Response == null || allServices.Response.Length == 0)
            {
                _logger.LogDebug("No services found in Consul matching filters");
                return discoveredUpstreams;
            }

            _logger.LogDebug("Found {ServiceCount} services in Consul", allServices.Response.Length);

            // Process each service and create upstream configurations
            var upstreamNames = new HashSet<string>(StringComparer.Ordinal);
            var manifestTasks = new List<Task<(string serviceName, ContextifyGatewayUpstreamEntity? upstream)>>();

            foreach (var serviceName in allServices.Response)
            {
                if (!ShouldDiscoverService(serviceName))
                {
                    continue;
                }

                manifestTasks.Add(ProcessServiceAsync(serviceName, upstreamNames, cancellationToken));
            }

            // Wait for all manifest fetches to complete (with throttling)
            var results = await ThrottledWhenAll(manifestTasks, _options.MaxConcurrentManifestFetches, cancellationToken).ConfigureAwait(false);

            // Collect valid upstreams
            foreach (var result in results)
            {
                if (result.upstream != null)
                {
                    discoveredUpstreams.Add(result.upstream);
                }
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "Consul discovery completed in {ElapsedMs}ms. Discovered {UpstreamCount} upstreams from {ServiceCount} services",
                stopwatch.ElapsedMilliseconds,
                discoveredUpstreams.Count,
                allServices.Response.Length);

            return discoveredUpstreams;
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    /// <summary>
    /// Gets a change token for monitoring when discovered upstreams may have changed.
    /// Returns a token that triggers after each polling interval for periodic refresh.
    /// Callers should register callbacks to trigger discovery refresh operations.
    /// </summary>
    /// <returns>A change token for watching discovery changes.</returns>
    public IChangeToken? Watch()
    {
        // Start a background timer to signal changes at polling interval
        StartPollingTimer();
        return _changeTokenSource.Token;
    }

    private Timer? _pollingTimer;

    /// <summary>
    /// Starts the polling timer for periodic change notifications.
    /// Creates a timer that triggers at the configured polling interval.
    /// </summary>
    private void StartPollingTimer()
    {
        if (_pollingTimer != null)
        {
            return; // Already started
        }

        _pollingTimer = new Timer(
            callback: _ => OnPollingTick(),
            state: null,
            dueTime: _options.PollInterval,
            period: _options.PollInterval);

        _logger.LogDebug(
            "Started polling timer with interval {Interval}s",
            _options.PollInterval.TotalSeconds);
    }

    /// <summary>
    /// Callback invoked when the polling timer ticks.
    /// Signals the change token to notify registered callbacks.
    /// </summary>
    private void OnPollingTick()
    {
        _logger.LogDebug("Polling tick: signaling change notification");

        // Reset the cancellation token source to signal changes
        var oldTokenSource = _changeTokenSource;
        _changeTokenSource.Reset();

        _logger.LogDebug("Change notification signaled");
    }

    /// <summary>
    /// Queries the Consul catalog for all registered services.
    /// Returns the complete list of service names from the catalog.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the query response with service names.</returns>
    private async Task<QueryResult<string[]>> QueryConsulServicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var queryOptions = new QueryOptions
            {
                WaitTime = _options.PollInterval,
                WaitIndex = 0 // First query
            };

            var result = await _consulClient.Catalog.Services(queryOptions, cancellationToken).ConfigureAwait(false);
            return new QueryResult<string[]>
            {
                Response = result.Response.Keys.ToArray(),
                LastIndex = result.LastIndex,
                RequestTime = result.RequestTime,
                KnownLeader = result.KnownLeader
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying Consul catalog for services");
            throw;
        }
    }

    /// <summary>
    /// Determines whether a service should be discovered based on configured filters.
    /// Applies tag filter and name prefix filter to the service name.
    /// </summary>
    /// <param name="serviceName">The service name to evaluate.</param>
    /// <returns>True if the service should be discovered; otherwise, false.</returns>
    private bool ShouldDiscoverService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return false;
        }

        // Apply service name prefix filter
        if (!string.IsNullOrWhiteSpace(_options.ServiceNamePrefix))
        {
            if (!serviceName.StartsWith(_options.ServiceNamePrefix, StringComparison.Ordinal))
            {
                _logger.LogTrace("Skipping service {ServiceName}: does not match prefix filter", serviceName);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Processes a single service to create an upstream configuration.
    /// Queries service health, filters by tags, and fetches the manifest if enabled.
    /// </summary>
    /// <param name="serviceName">The service name to process.</param>
    /// <param name="discoveredNames">Set of already discovered upstream names for deduplication.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding a tuple with service name and optional upstream entity.</returns>
    private async Task<(string serviceName, ContextifyGatewayUpstreamEntity? upstream)> ProcessServiceAsync(
        string serviceName,
        HashSet<string> discoveredNames,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogTrace("Processing service: {ServiceName}", serviceName);

            // Get healthy service instances with tag filtering
            var healthResult = await _consulClient.Health.Service(
                serviceName,
                _options.ServiceTagFilter,
                true,
                new QueryOptions(),
                cancellationToken).ConfigureAwait(false);

            if (healthResult.Response == null || healthResult.Response.Length == 0)
            {
                _logger.LogTrace("No healthy instances found for service: {ServiceName}", serviceName);
                return (serviceName, null);
            }

            _logger.LogTrace("Found {InstanceCount} healthy instances for service: {ServiceName}",
                healthResult.Response.Length, serviceName);

            // Use the first healthy instance (could be enhanced for load balancing)
            var serviceInstance = healthResult.Response[0];
            var serviceAddress = BuildServiceAddress(serviceInstance);

            if (_options.FetchManifests)
            {
                // Fetch and parse the manifest
                var manifest = await FetchManifestAsync(serviceAddress, cancellationToken).ConfigureAwait(false);
                if (manifest == null || !manifest.IsValid())
                {
                    _logger.LogWarning("Invalid or missing manifest for service: {ServiceName}", serviceName);
                    return (serviceName, null);
                }

                // Build upstream from manifest
                var upstream = BuildUpstreamFromManifest(serviceName, serviceAddress, manifest);

                // Check for duplicate upstream names
                if (discoveredNames.Contains(upstream.UpstreamName))
                {
                    _logger.LogWarning(
                        "Duplicate upstream name detected: {UpstreamName}. Skipping service: {ServiceName}",
                        upstream.UpstreamName, serviceName);
                    return (serviceName, null);
                }

                discoveredNames.Add(upstream.UpstreamName);
                _logger.LogTrace("Created upstream {UpstreamName} from service: {ServiceName}",
                    upstream.UpstreamName, serviceName);

                return (serviceName, upstream);
            }
            else
            {
                // Build upstream without manifest (basic configuration)
                var upstream = BuildUpstreamWithoutManifest(serviceName, serviceAddress);

                // Check for duplicate upstream names
                if (discoveredNames.Contains(upstream.UpstreamName))
                {
                    _logger.LogWarning(
                        "Duplicate upstream name detected: {UpstreamName}. Skipping service: {ServiceName}",
                        upstream.UpstreamName, serviceName);
                    return (serviceName, null);
                }

                discoveredNames.Add(upstream.UpstreamName);
                _logger.LogTrace("Created upstream {UpstreamName} from service (no manifest): {ServiceName}",
                    upstream.UpstreamName, serviceName);

                return (serviceName, upstream);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing service: {ServiceName}", serviceName);
            return (serviceName, null);
        }
    }

    /// <summary>
    /// Builds the service address from a Consul service instance.
    /// Uses the service address or service address from the health check.
    /// </summary>
    /// <param name="serviceEntry">The Consul service entry.</param>
    /// <returns>The constructed service URI.</returns>
    private Uri BuildServiceAddress(ServiceEntry serviceEntry)
    {
        var scheme = _options.UseHttpsForManifests ? "https" : "http";
        var host = serviceEntry.Service.Address;
        var port = serviceEntry.Service.Port;

        // Use service address if provided, otherwise use node address
        if (string.IsNullOrWhiteSpace(host))
        {
            host = serviceEntry.Node.Address;
        }

        return new Uri($"{scheme}://{host}:{port}");
    }

    /// <summary>
    /// Fetches the Contextify manifest from a service instance.
    /// Makes an HTTP GET request to the manifest path and deserializes the response.
    /// </summary>
    /// <param name="serviceAddress">The base address of the service.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the manifest entity, or null if fetch fails.</returns>
    private async Task<ContextifyServiceManifestEntity?> FetchManifestAsync(
        Uri serviceAddress,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifestUrl = new Uri(serviceAddress, _options.ManifestPath);
            _logger.LogTrace("Fetching manifest from: {ManifestUrl}", manifestUrl);

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = _options.RequestTimeout;

            var response = await httpClient.GetAsync(manifestUrl, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Manifest fetch failed with status {StatusCode}: {ManifestUrl}",
                    response.StatusCode, manifestUrl);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<ContextifyServiceManifestEntity>(
                content,
                _jsonSerializerOptions);

            if (manifest == null)
            {
                _logger.LogWarning("Failed to deserialize manifest from: {ManifestUrl}", manifestUrl);
                return null;
            }

            _logger.LogTrace("Successfully fetched manifest from: {ManifestUrl}", manifestUrl);
            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching manifest from: {ServiceAddress}", serviceAddress);
            return null;
        }
    }

    /// <summary>
    /// Builds an upstream configuration entity from a service manifest.
    /// Uses manifest values with fallbacks to service information when not specified.
    /// </summary>
    /// <param name="serviceName">The Consul service name.</param>
    /// <param name="serviceAddress">The service base address.</param>
    /// <param name="manifest">The fetched manifest entity.</param>
    /// <returns>The constructed upstream configuration entity.</returns>
    private ContextifyGatewayUpstreamEntity BuildUpstreamFromManifest(
        string serviceName,
        Uri serviceAddress,
        ContextifyServiceManifestEntity manifest)
    {
        var upstream = new ContextifyGatewayUpstreamEntity
        {
            // Use manifest service name or fallback to Consul service name
            UpstreamName = !string.IsNullOrWhiteSpace(manifest.ServiceName)
                ? manifest.ServiceName
                : serviceName,

            // Build MCP endpoint from manifest or service address
            McpHttpEndpoint = BuildMcpEndpoint(serviceAddress, manifest.McpHttpEndpoint),

            // Use manifest namespace prefix or fallback to default
            NamespacePrefix = !string.IsNullOrWhiteSpace(manifest.NamespacePrefix)
                ? manifest.NamespacePrefix
                : (!string.IsNullOrWhiteSpace(_options.DefaultNamespacePrefix)
                    ? _options.DefaultNamespacePrefix
                    : (!string.IsNullOrWhiteSpace(manifest.ServiceName)
                        ? manifest.ServiceName
                        : serviceName)),

            // Apply manifest timeout if specified
            RequestTimeout = manifest.RequestTimeoutSeconds.HasValue
                ? TimeSpan.FromSeconds(manifest.RequestTimeoutSeconds.Value)
                : TimeSpan.FromSeconds(30),

            Enabled = true
        };

        return upstream;
    }

    /// <summary>
    /// Builds an upstream configuration entity without a manifest.
    /// Uses basic service information for minimal configuration.
    /// </summary>
    /// <param name="serviceName">The Consul service name.</param>
    /// <param name="serviceAddress">The service base address.</param>
    /// <returns>The constructed upstream configuration entity.</returns>
    private ContextifyGatewayUpstreamEntity BuildUpstreamWithoutManifest(
        string serviceName,
        Uri serviceAddress)
    {
        return new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = serviceName,
            McpHttpEndpoint = serviceAddress,
            NamespacePrefix = !string.IsNullOrWhiteSpace(_options.DefaultNamespacePrefix)
                ? _options.DefaultNamespacePrefix
                : serviceName,
            RequestTimeout = TimeSpan.FromSeconds(30),
            Enabled = true
        };
    }

    /// <summary>
    /// Builds the MCP HTTP endpoint URI from service address and manifest endpoint.
    /// Handles both absolute URLs and relative paths from the manifest.
    /// </summary>
    /// <param name="serviceAddress">The service base address.</param>
    /// <param name="manifestEndpoint">The endpoint from manifest (may be relative or absolute).</param>
    /// <returns>The constructed MCP endpoint URI.</returns>
    private Uri BuildMcpEndpoint(Uri serviceAddress, string? manifestEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(manifestEndpoint))
        {
            // If manifest endpoint is an absolute URL, use it directly
            if (Uri.TryCreate(manifestEndpoint, UriKind.Absolute, out var absoluteEndpoint))
            {
                return absoluteEndpoint;
            }

            // Otherwise, treat as relative path and append to service address
            return new Uri(serviceAddress, manifestEndpoint);
        }

        // Default to service root
        return serviceAddress;
    }

    /// <summary>
    /// Executes multiple tasks with throttling to limit concurrent operations.
    /// Prevents overwhelming services with too many concurrent manifest fetch requests.
    /// </summary>
    /// <typeparam name="T">The task result type.</typeparam>
    /// <param name="tasks">The collection of tasks to execute.</param>
    /// <param name="maxConcurrency">Maximum number of concurrent tasks.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding all results when complete.</returns>
    private static async Task<List<T>> ThrottledWhenAll<T>(
        List<Task<T>> tasks,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        var results = new List<T>(tasks.Count);
        var executingTasks = new HashSet<Task<T>>();
        var taskQueue = new Queue<Task<T>>(tasks);

        while (taskQueue.Count > 0 || executingTasks.Count > 0)
        {
            while (executingTasks.Count < maxConcurrency && taskQueue.Count > 0)
            {
                var task = taskQueue.Dequeue();
                executingTasks.Add(task);
            }

            var completedTask = await Task.WhenAny(executingTasks).ConfigureAwait(false);
            executingTasks.Remove(completedTask);

            // Propagate cancellation
            cancellationToken.ThrowIfCancellationRequested();

            results.Add(await completedTask.ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>
    /// Disposes of resources used by the Consul discovery provider.
    /// Ensures the Consul client, timer, and semaphore are properly disposed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _pollingTimer?.Dispose();
        _changeTokenSource?.Dispose();
        _consulClient?.Dispose();
        _discoveryLock?.Dispose();

        _disposed = true;

        _logger.LogDebug("Consul discovery provider disposed");
    }
}

/// <summary>
/// Provides a change token that can be signaled manually via cancellation.
/// Used for triggering change notifications on a polling schedule.
/// </summary>
internal sealed class CancellationChangeTokenSource
{
    private CancellationTokenSource _cts = new();

    /// <summary>
    /// Gets the change token for signaling changes.
    /// Returns a new token each time Reset is called.
    /// </summary>
    public IChangeToken Token => new CancellationChangeToken(_cts.Token);

    /// <summary>
    /// Resets the cancellation token source to signal a change.
    /// Creates a new token and cancels the old one to trigger callbacks.
    /// </summary>
    public void Reset()
    {
        var oldCts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        oldCts.Cancel();
        oldCts.Dispose();
    }

    /// <summary>
    /// Disposes of the cancellation token source.
    /// </summary>
    public void Dispose()
    {
        _cts?.Dispose();
    }
}
