namespace Contextify.Core.Catalog;

/// <summary>
/// Immutable snapshot of the tool catalog at a specific point in time.
/// Provides a consistent, read-only view of all available tools and their configurations.
/// Snapshots are atomically swapped to ensure readers never see partially updated state.
/// Designed for high-throughput concurrent access with zero locking on read paths.
/// </summary>
public sealed class ContextifyToolCatalogSnapshotEntity
{
    /// <summary>
    /// Gets the UTC timestamp when this snapshot was created.
    /// Used for snapshot age tracking and cache invalidation decisions.
    /// </summary>
    public DateTime CreatedUtc { get; }

    /// <summary>
    /// Gets the source version identifier for this snapshot.
    /// Represents the version of the policy/configuration source used to build this snapshot.
    /// Used for detecting configuration changes and triggering reloads.
    /// Null value indicates no versioning is configured.
    /// </summary>
    public string? PolicySourceVersion { get; }

    /// <summary>
    /// Gets the read-only dictionary of tools indexed by their name.
    /// Provides O(1) lookup for tool descriptors by name.
    /// The dictionary is immutable to prevent modifications after snapshot creation.
    /// </summary>
    public IReadOnlyDictionary<string, ContextifyToolDescriptorEntity> ToolsByName { get; }

    /// <summary>
    /// Initializes a new instance with complete snapshot information.
    /// The tools dictionary is copied to ensure immutability.
    /// </summary>
    /// <param name="createdUtc">The UTC timestamp when the snapshot was created.</param>
    /// <param name="policySourceVersion">The source version identifier.</param>
    /// <param name="toolsByName">The dictionary of tools indexed by name.</param>
    /// <exception cref="ArgumentNullException">Thrown when toolsByName is null.</exception>
    public ContextifyToolCatalogSnapshotEntity(
        DateTime createdUtc,
        string? policySourceVersion,
        IReadOnlyDictionary<string, ContextifyToolDescriptorEntity> toolsByName)
    {
        if (toolsByName is null)
        {
            throw new ArgumentNullException(nameof(toolsByName));
        }

        CreatedUtc = createdUtc;
        PolicySourceVersion = policySourceVersion;
        ToolsByName = new Dictionary<string, ContextifyToolDescriptorEntity>(toolsByName, StringComparer.Ordinal);
    }

    /// <summary>
    /// Creates an empty snapshot with no tools.
    /// Useful as the initial snapshot before any tools are discovered.
    /// </summary>
    /// <param name="policySourceVersion">Optional source version identifier.</param>
    /// <returns>A new empty snapshot.</returns>
    public static ContextifyToolCatalogSnapshotEntity Empty(string? policySourceVersion = null)
    {
        return new ContextifyToolCatalogSnapshotEntity(
            createdUtc: DateTime.UtcNow,
            policySourceVersion: policySourceVersion,
            toolsByName: new Dictionary<string, ContextifyToolDescriptorEntity>(StringComparer.Ordinal));
    }

    /// <summary>
    /// Attempts to get a tool descriptor by name.
    /// </summary>
    /// <param name="toolName">The name of the tool to retrieve.</param>
    /// <param name="toolDescriptor">The tool descriptor if found; otherwise, null.</param>
    /// <returns>True if the tool was found; otherwise, false.</returns>
    public bool TryGetTool(string toolName, out ContextifyToolDescriptorEntity? toolDescriptor)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            toolDescriptor = null;
            return false;
        }

        return ToolsByName.TryGetValue(toolName, out toolDescriptor);
    }

    /// <summary>
    /// Gets the count of tools in this snapshot.
    /// </summary>
    public int ToolCount => ToolsByName.Count;

    /// <summary>
    /// Gets all tool names in this snapshot.
    /// </summary>
    public IEnumerable<string> ToolNames => ToolsByName.Keys;

    /// <summary>
    /// Gets all tool descriptors in this snapshot.
    /// </summary>
    public IEnumerable<ContextifyToolDescriptorEntity> AllTools => ToolsByName.Values;

    /// <summary>
    /// Validates the snapshot configuration.
    /// Ensures all tool descriptors are valid and the snapshot is consistent.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when snapshot is invalid.</exception>
    public void Validate()
    {
        foreach (var kvp in ToolsByName)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
            {
                throw new InvalidOperationException("Tool name cannot be null or whitespace.");
            }

            if (kvp.Value is null)
            {
                throw new InvalidOperationException($"Tool descriptor for key '{kvp.Key}' is null.");
            }

            // Validate tool name matches dictionary key
            if (!string.Equals(kvp.Key, kvp.Value.ToolName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Tool name mismatch: dictionary key '{kvp.Key}' does not match descriptor name '{kvp.Value.ToolName}'.");
            }

            kvp.Value.Validate();
        }
    }

    /// <summary>
    /// Creates a deep copy of the current snapshot.
    /// All tool descriptors are deep copied to ensure complete isolation.
    /// </summary>
    /// <returns>A new snapshot entity with copied values.</returns>
    public ContextifyToolCatalogSnapshotEntity DeepCopy()
    {
        var copiedTools = new Dictionary<string, ContextifyToolDescriptorEntity>(StringComparer.Ordinal);

        foreach (var kvp in ToolsByName)
        {
            copiedTools[kvp.Key] = kvp.Value.DeepCopy();
        }

        return new ContextifyToolCatalogSnapshotEntity(
            createdUtc: CreatedUtc,
            policySourceVersion: PolicySourceVersion,
            toolsByName: copiedTools);
    }
}
