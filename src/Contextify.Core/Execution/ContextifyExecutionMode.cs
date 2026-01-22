namespace Contextify.Core.Execution;

/// <summary>
/// Defines the execution mode for tool invocation in the Contextify framework.
/// Controls how and where tool endpoints are called, supporting both local and remote execution scenarios.
/// </summary>
public sealed class ContextifyExecutionMode
{
    /// <summary>
    /// Gets the unique identifier for this execution mode.
    /// Used for mode registration and lookup in the executor service.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the human-readable display name for this execution mode.
    /// Used in logging, diagnostics, and UI components.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets a value indicating whether this execution mode calls endpoints within the same application process.
    /// In-process modes typically use IHttpClientFactory with a local base address.
    /// </summary>
    public bool IsInProcess { get; }

    /// <summary>
    /// Gets a value indicating whether this execution mode calls endpoints on a remote server.
    /// Remote modes require network configuration and may involve authentication headers.
    /// </summary>
    public bool IsRemote => !IsInProcess;

    /// <summary>
    /// Initializes a new instance with the specified mode properties.
    /// </summary>
    /// <param name="name">The unique identifier for the mode (e.g., "InProcessHttp", "RemoteHttp").</param>
    /// <param name="displayName">The human-readable display name.</param>
    /// <param name="isInProcess">Whether this is an in-process execution mode.</param>
    /// <exception cref="ArgumentException">Thrown when name or displayName is null or whitespace.</exception>
    private ContextifyExecutionMode(string name, string displayName, bool isInProcess)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Execution mode name cannot be null or whitespace.", nameof(name));
        }
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Execution mode display name cannot be null or whitespace.", nameof(displayName));
        }

        Name = name;
        DisplayName = displayName;
        IsInProcess = isInProcess;
    }

    /// <summary>
    /// In-process HTTP execution mode (default for ASP.NET Core applications).
    /// Uses IHttpClientFactory to create a named client configured to call the local application base address.
    /// Ideal for monolithic applications where tools are exposed as endpoints within the same process.
    /// </summary>
    public static readonly ContextifyExecutionMode InProcessHttp = new(
        name: "InProcessHttp",
        displayName: "In-Process HTTP",
        isInProcess: true);

    /// <summary>
    /// Remote HTTP execution mode (to be implemented in future versions).
    /// Will allow calling tool endpoints on remote servers with full network configuration support.
    /// Useful for microservice architectures and distributed tool execution scenarios.
    /// </summary>
    public static readonly ContextifyExecutionMode RemoteHttp = new(
        name: "RemoteHttp",
        displayName: "Remote HTTP",
        isInProcess: false);

    /// <summary>
    /// Gets a read-only collection of all available execution modes.
    /// Useful for validation, enumeration, and UI selection scenarios.
    /// </summary>
    public static IReadOnlyCollection<ContextifyExecutionMode> AllModes => new[] { InProcessHttp, RemoteHttp };

    /// <summary>
    /// Attempts to find an execution mode by its unique name.
    /// </summary>
    /// <param name="name">The name of the execution mode to find.</param>
    /// <returns>The matching execution mode, or null if not found.</returns>
    public static ContextifyExecutionMode? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return AllModes.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the string representation of this execution mode (its name).
    /// </summary>
    /// <returns>The unique name identifier of this execution mode.</returns>
    public override string ToString() => Name;

    /// <summary>
    /// Determines whether the specified execution mode is equal to the current mode.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>True if the objects are equal execution modes; false otherwise.</returns>
    public override bool Equals(object? obj)
    {
        return obj is ContextifyExecutionMode other && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a hash code for this execution mode based on its name.
    /// </summary>
    /// <returns>A hash code for the current execution mode.</returns>
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name ?? string.Empty);

    /// <summary>
    /// Determines whether two execution modes are equal.
    /// </summary>
    /// <param name="left">The left execution mode.</param>
    /// <param name="right">The right execution mode.</param>
    /// <returns>True if the modes are equal; false otherwise.</returns>
    public static bool operator ==(ContextifyExecutionMode? left, ContextifyExecutionMode? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two execution modes are not equal.
    /// </summary>
    /// <param name="left">The left execution mode.</param>
    /// <param name="right">The right execution mode.</param>
    /// <returns>True if the modes are not equal; false otherwise.</returns>
    public static bool operator !=(ContextifyExecutionMode? left, ContextifyExecutionMode? right)
    {
        return !(left == right);
    }
}
