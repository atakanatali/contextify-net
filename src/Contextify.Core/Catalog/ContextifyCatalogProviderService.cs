using System.Diagnostics;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Rules.Catalog;
using Microsoft.Extensions.Logging;

namespace Contextify.Core.Catalog;

/// <summary>
/// Service providing atomic tool catalog snapshots with cache-coherent reload capabilities.
/// Maintains a volatile reference to the current snapshot that can be swapped atomically
/// using Interlocked.Exchange, ensuring zero contention on read paths.
/// Reloads are throttled based on minimum interval and source version changes.
/// </summary>
public sealed class ContextifyCatalogProviderService
{
    /// <summary>
    /// The minimum interval between snapshot reloads.
    /// Prevents excessive reloading from rapid configuration changes.
    /// Default value is 1 second.
    /// </summary>
    private const int DefaultMinReloadIntervalMilliseconds = 1000;

    /// <summary>
    /// Gets the logger instance for diagnostics and tracing.
    /// </summary>
    private readonly ILogger<ContextifyCatalogProviderService> _logger;

    /// <summary>
    /// Gets the minimum interval between reload operations in milliseconds.
    /// Prevents reload thrashing from rapid configuration changes.
    /// </summary>
    private readonly int _minReloadIntervalMilliseconds;

    /// <summary>
    /// Gets the policy configuration provider for fetching tool and policy data.
    /// </summary>
    private readonly IContextifyPolicyConfigProvider _policyConfigProvider;

    /// <summary>
    /// The catalog builder service using rule engine for validation logic.
    /// </summary>
    private readonly ContextifyCatalogBuilderService _catalogBuilder;

    /// <summary>
    /// The volatile reference to the current snapshot.
    /// Volatile ensures reads/writes are not reordered or cached inconsistently.
    /// All reads use Volatile.Read for safe concurrent access without locks.
    /// All writes use Volatile.Write or Interlocked.Exchange for atomic updates.
    /// </summary>
    private ContextifyToolCatalogSnapshotEntity _volatileSnapshot;

    /// <summary>
    /// The UTC timestamp of the last successful reload operation.
    /// Used for throttling reloads based on minimum interval.
    /// </summary>
    private DateTime _lastReloadUtc;

    /// <summary>
    /// The source version from the last reload.
    /// Used to detect configuration changes for reload triggering.
    /// </summary>
    private string? _lastSourceVersion;

    /// <summary>
    /// Gets the current snapshot atomically without acquiring locks.
    /// Uses Volatile.Read to ensure the most recent value is observed.
    /// </summary>
    /// <returns>The current catalog snapshot.</returns>
    /// <remarks>
    /// This method is wait-free and suitable for high-frequency calls.
    /// Multiple threads can call this method concurrently without contention.
    /// The returned snapshot is immutable and safe to cache for the duration of an operation.
    /// </remarks>
    public ContextifyToolCatalogSnapshotEntity GetSnapshot()
    {
        var snapshot = Volatile.Read(ref _volatileSnapshot);
        return snapshot;
    }

    /// <summary>
    /// Initializes a new instance with the specified dependencies and configuration.
    /// </summary>
    /// <param name="policyConfigProvider">The policy configuration provider.</param>
    /// <param name="logger">The logger instance for diagnostics.</param>
    /// <param name="catalogBuilder">Optional catalog builder service. If null, creates a default instance.</param>
    /// <param name="minReloadIntervalMilliseconds">
    /// The minimum interval between reloads in milliseconds.
    /// If null, uses the default value of 1000ms.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyCatalogProviderService(
        IContextifyPolicyConfigProvider policyConfigProvider,
        ILogger<ContextifyCatalogProviderService> logger,
        ContextifyCatalogBuilderService? catalogBuilder = null,
        int? minReloadIntervalMilliseconds = null)
    {
        _policyConfigProvider = policyConfigProvider ?? throw new ArgumentNullException(nameof(policyConfigProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _catalogBuilder = catalogBuilder ?? new ContextifyCatalogBuilderService(null);
        _minReloadIntervalMilliseconds = minReloadIntervalMilliseconds ?? DefaultMinReloadIntervalMilliseconds;

        // Initialize with an empty snapshot to avoid null checks
        _volatileSnapshot = ContextifyToolCatalogSnapshotEntity.Empty();
        _lastReloadUtc = DateTime.MinValue;
        _lastSourceVersion = null;
    }

    /// <summary>
    /// Forces a reload of the catalog snapshot, building a new snapshot atomically.
    /// The new snapshot is swapped into place using Interlocked.Exchange, ensuring
    /// concurrent readers either see the old snapshot or the new one, never a partially built state.
    /// </summary>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>
    /// A task representing the async operation.
    /// The task result contains the newly created snapshot.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <exception cref="InvalidOperationException">Thrown when snapshot building fails.</exception>
    /// <remarks>
    /// This method performs the following steps:
    /// 1. Fetches the latest policy configuration from the provider
    /// 2. Builds a new snapshot from the configuration
    /// 3. Validates the new snapshot
    /// 4. Atomically swaps the new snapshot into place
    ///
    /// Concurrent readers continue to use the old snapshot until the swap completes.
    /// The old snapshot is eligible for garbage collection after all readers release it.
    /// </remarks>
    public async Task<ContextifyToolCatalogSnapshotEntity> ReloadAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Starting catalog snapshot reload at {Timestamp}", DateTime.UtcNow);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Step 1: Fetch the latest policy configuration
            var policyConfig = await _policyConfigProvider.GetAsync(ct).ConfigureAwait(false);

            // Step 2: Build the new snapshot from policy configuration
            var newSnapshot = BuildSnapshotFromPolicy(policyConfig);

            // Step 3: Validate the snapshot before making it live
            newSnapshot.Validate();

            // Step 4: Atomically swap the new snapshot into place
            // Interlocked.Exchange ensures the swap is atomic and all threads
            // see either the old snapshot or the new one, never a partial state
            var oldSnapshot = Interlocked.Exchange(ref _volatileSnapshot, newSnapshot);

            // Update tracking state after successful swap
            _lastReloadUtc = DateTime.UtcNow;
            _lastSourceVersion = newSnapshot.PolicySourceVersion;

            stopwatch.Stop();

            _logger.LogInformation(
                "Catalog snapshot reloaded successfully at {Timestamp}. " +
                "Tool count: {ToolCount}, Source version: {SourceVersion}, " +
                "Duration: {DurationMs}ms",
                DateTime.UtcNow,
                newSnapshot.ToolCount,
                newSnapshot.PolicySourceVersion ?? "none",
                stopwatch.ElapsedMilliseconds);

            return newSnapshot;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Catalog snapshot reload was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload catalog snapshot.");
            throw new InvalidOperationException("Failed to reload catalog snapshot.", ex);
        }
    }

    /// <summary>
    /// Ensures the snapshot is fresh, reloading only if the minimum interval has passed
    /// or the source version has changed since the last reload.
    /// Uses throttling to prevent excessive reloads from rapid configuration changes.
    /// </summary>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>
    /// A task representing the async operation.
    /// The task result contains the current snapshot (may be existing or newly reloaded).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// This method implements the following throttling logic:
    /// 1. If insufficient time has passed since the last reload, return the current snapshot
    /// 2. If the source version hasn't changed, return the current snapshot
    /// 3. Otherwise, trigger a reload and return the new snapshot
    ///
    /// The reload decision is made atomically to prevent multiple concurrent reloads.
    /// </remarks>
    public async Task<ContextifyToolCatalogSnapshotEntity> EnsureFreshSnapshotAsync(CancellationToken ct = default)
    {
        var currentSnapshot = GetSnapshot();
        var now = DateTime.UtcNow;
        var timeSinceLastReload = now - _lastReloadUtc;

        // Check if we need to reload based on time interval
        bool needsReloadByTime = timeSinceLastReload.TotalMilliseconds >= _minReloadIntervalMilliseconds;

        // Check if we need to reload based on source version change
        // We need to fetch the current config to check the version
        bool needsReloadByVersion = false;

        if (!needsReloadByTime)
        {
            _logger.LogDebug(
                "Snapshot reload skipped due to minimum interval. " +
                "Time since last reload: {TimeSinceLastReload}ms, " +
                "Minimum interval: {MinInterval}ms",
                timeSinceLastReload.TotalMilliseconds,
                _minReloadIntervalMilliseconds);

            return currentSnapshot;
        }

        // Fetch the current config to check version
        try
        {
            var currentConfig = await _policyConfigProvider.GetAsync(ct).ConfigureAwait(false);
            needsReloadByVersion = !string.Equals(
                currentConfig.SourceVersion,
                _lastSourceVersion,
                StringComparison.Ordinal);

            if (!needsReloadByVersion)
            {
                _logger.LogDebug(
                    "Snapshot reload skipped due to unchanged source version. " +
                    "Current version: {SourceVersion}",
                    currentConfig.SourceVersion ?? "none");

                // Still update last reload time to prevent continuous polling
                _lastReloadUtc = now;
                return currentSnapshot;
            }

            _logger.LogDebug(
                "Source version changed from '{OldVersion}' to '{NewVersion}'. Triggering reload.",
                _lastSourceVersion ?? "none",
                currentConfig.SourceVersion ?? "none");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to check source version. Proceeding with reload.");
            // If we can't check the version, err on the side of reloading
        }

        // Perform the reload
        return await ReloadAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a new catalog snapshot from the provided policy configuration.
    /// Uses the rule engine to validate and process policies.
    /// </summary>
    /// <param name="policyConfig">The policy configuration to build from.</param>
    /// <returns>A new catalog snapshot containing tools from the policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when policyConfig is null.</exception>
    /// <remarks>
    /// This method delegates to the catalog builder service which uses a rule engine
    /// for validation. The rule engine applies the following rules in priority order:
    /// 1. Enabled policy validation - skips disabled policies
    /// 2. Tool name validation - skips policies without tool names
    /// 3. Duplicate detection - skips duplicate tool names
    ///
    /// This approach provides better testability and extensibility compared to
    /// the previous if-statement based implementation.
    /// </remarks>
    private ContextifyToolCatalogSnapshotEntity BuildSnapshotFromPolicy(ContextifyPolicyConfigDto policyConfig)
    {
        return _catalogBuilder.BuildSnapshotFromPolicy(policyConfig);
    }
}
