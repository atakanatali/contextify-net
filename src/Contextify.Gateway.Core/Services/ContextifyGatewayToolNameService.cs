using Contextify.Gateway.Core.Configuration;

namespace Contextify.Gateway.Core.Services;

/// <summary>
/// Service for deterministic tool name translation between gateway and upstream namespaces.
/// Handles bidirectional mapping of tool names with configurable namespace separators.
/// Ensures consistent and predictable tool naming across the gateway aggregation layer.
/// </summary>
public sealed class ContextifyGatewayToolNameService
{
    /// <summary>
    /// Gets the separator used between namespace prefix and upstream tool name.
    /// Must be a non-empty string to enable proper parsing of external tool names.
    /// </summary>
    public string Separator { get; }

    /// <summary>
    /// Initializes a new instance of the ContextifyGatewayToolNameService class.
    /// Creates a service instance with the specified separator for tool name construction.
    /// </summary>
    /// <param name="separator">The separator to use between namespace and tool name. Defaults to "."</param>
    /// <exception cref="ArgumentException">Thrown when separator is null or whitespace.</exception>
    public ContextifyGatewayToolNameService(string separator = ".")
    {
        if (string.IsNullOrWhiteSpace(separator))
        {
            throw new ArgumentException("Separator cannot be null or whitespace.", nameof(separator));
        }

        Separator = separator;
    }

    /// <summary>
    /// Converts an internal upstream tool name to an external gateway tool name.
    /// The external name format is: {namespacePrefix}{separator}{upstreamToolName}.
    /// The namespace prefix is always applied, even if the upstream tool name already contains the separator.
    /// This ensures deterministic namespacing and avoids naming conflicts across upstreams.
    /// </summary>
    /// <param name="namespacePrefix">The namespace prefix for the upstream. Must contain only valid characters.</param>
    /// <param name="upstreamToolName">The original tool name from the upstream server.</param>
    /// <returns>The external tool name with namespace prefix applied.</returns>
    /// <exception cref="ArgumentException">Thrown when namespacePrefix or upstreamToolName is null/whitespace.</exception>
    /// <exception cref="ArgumentException">Thrown when namespacePrefix contains invalid characters.</exception>
    public string ToExternalName(string namespacePrefix, string upstreamToolName)
    {
        ValidateNamespacePrefix(namespacePrefix);
        ValidateUpstreamToolName(upstreamToolName);

        // Always prefix the tool name with the namespace prefix and separator
        // This ensures deterministic naming regardless of the upstream tool name format
        return $"{namespacePrefix}{Separator}{upstreamToolName}";
    }

    /// <summary>
    /// Converts an external gateway tool name back to the original upstream tool name.
    /// Strips the namespace prefix and separator from the external name to recover the upstream tool name.
    /// </summary>
    /// <param name="upstreamName">The upstream name used to identify the correct namespace prefix.</param>
    /// <param name="externalToolName">The external tool name from the gateway catalog.</param>
    /// <returns>The original upstream tool name without the namespace prefix.</returns>
    /// <exception cref="ArgumentException">Thrown when upstreamName or externalToolName is null/whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the external tool name does not start with the expected namespace prefix.</exception>
    public string ToInternalName(string upstreamName, string externalToolName)
    {
        if (string.IsNullOrWhiteSpace(upstreamName))
        {
            throw new ArgumentException("Upstream name cannot be null or whitespace.", nameof(upstreamName));
        }

        if (string.IsNullOrWhiteSpace(externalToolName))
        {
            throw new ArgumentException("External tool name cannot be null or whitespace.", nameof(externalToolName));
        }

        // Validate the namespace prefix (using upstreamName as the prefix)
        ValidateNamespacePrefix(upstreamName);

        // Build the expected prefix with separator
        string expectedPrefix = $"{upstreamName}{Separator}";

        // Check if the external tool name starts with the expected prefix
        if (!externalToolName.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"External tool name '{externalToolName}' does not start with expected prefix '{expectedPrefix}'. " +
                $"Unable to extract internal tool name for upstream '{upstreamName}'.");
        }

        // Extract and return the internal tool name (everything after the prefix)
        string internalName = externalToolName.Substring(expectedPrefix.Length);

        if (string.IsNullOrWhiteSpace(internalName))
        {
            throw new InvalidOperationException(
                $"External tool name '{externalToolName}' results in an empty internal tool name after removing prefix '{expectedPrefix}'.");
        }

        return internalName;
    }

    /// <summary>
    /// Converts an external gateway tool name back to the original upstream tool name using a custom namespace prefix.
    /// Strips the namespace prefix and separator from the external name to recover the upstream tool name.
    /// </summary>
    /// <param name="namespacePrefix">The namespace prefix used for the upstream.</param>
    /// <param name="externalToolName">The external tool name from the gateway catalog.</param>
    /// <returns>The original upstream tool name without the namespace prefix.</returns>
    /// <exception cref="ArgumentException">Thrown when namespacePrefix or externalToolName is null/whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the external tool name does not start with the expected namespace prefix.</exception>
    public string ToInternalNameByPrefix(string namespacePrefix, string externalToolName)
    {
        ValidateNamespacePrefix(namespacePrefix);

        if (string.IsNullOrWhiteSpace(externalToolName))
        {
            throw new ArgumentException("External tool name cannot be null or whitespace.", nameof(externalToolName));
        }

        // Build the expected prefix with separator
        string expectedPrefix = $"{namespacePrefix}{Separator}";

        // Check if the external tool name starts with the expected prefix
        if (!externalToolName.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"External tool name '{externalToolName}' does not start with expected prefix '{expectedPrefix}'. " +
                $"Unable to extract internal tool name for namespace prefix '{namespacePrefix}'.");
        }

        // Extract and return the internal tool name (everything after the prefix)
        string internalName = externalToolName.Substring(expectedPrefix.Length);

        if (string.IsNullOrWhiteSpace(internalName))
        {
            throw new InvalidOperationException(
                $"External tool name '{externalToolName}' results in an empty internal tool name after removing prefix '{expectedPrefix}'.");
        }

        return internalName;
    }

    /// <summary>
    /// Validates that a namespace prefix contains only allowed characters.
    /// Valid characters are: a-z, A-Z, 0-9, dot (.), underscore (_), and hyphen (-).
    /// </summary>
    /// <param name="namespacePrefix">The namespace prefix to validate.</param>
    /// <exception cref="ArgumentException">Thrown when the namespace prefix is invalid.</exception>
    private static void ValidateNamespacePrefix(string namespacePrefix)
    {
        if (string.IsNullOrWhiteSpace(namespacePrefix))
        {
            throw new ArgumentException("Namespace prefix cannot be null or whitespace.", nameof(namespacePrefix));
        }

        foreach (char c in namespacePrefix)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
            {
                throw new ArgumentException(
                    $"Namespace prefix '{namespacePrefix}' contains invalid character '{c}'. " +
                    "Only letters, digits, dots, underscores, and hyphens are allowed.",
                    nameof(namespacePrefix));
            }
        }
    }

    /// <summary>
    /// Validates that an upstream tool name is not null or whitespace.
    /// </summary>
    /// <param name="upstreamToolName">The upstream tool name to validate.</param>
    /// <exception cref="ArgumentException">Thrown when the upstream tool name is invalid.</exception>
    private static void ValidateUpstreamToolName(string upstreamToolName)
    {
        if (string.IsNullOrWhiteSpace(upstreamToolName))
        {
            throw new ArgumentException("Upstream tool name cannot be null or whitespace.", nameof(upstreamToolName));
        }
    }

    /// <summary>
    /// Tests whether an external tool name belongs to a specific upstream.
    /// Checks if the external name starts with the expected namespace prefix and separator.
    /// </summary>
    /// <param name="namespacePrefix">The namespace prefix to check against.</param>
    /// <param name="externalToolName">The external tool name to test.</param>
    /// <returns>True if the external tool name belongs to the specified upstream; otherwise, false.</returns>
    public bool BelongsToUpstream(string namespacePrefix, string externalToolName)
    {
        if (string.IsNullOrWhiteSpace(namespacePrefix) || string.IsNullOrWhiteSpace(externalToolName))
        {
            return false;
        }

        // Validate the prefix format without throwing
        try
        {
            ValidateNamespacePrefix(namespacePrefix);
        }
        catch
        {
            return false;
        }

        string expectedPrefix = $"{namespacePrefix}{Separator}";
        return externalToolName.StartsWith(expectedPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts the namespace prefix from an external tool name.
    /// Parses the external name to find the prefix portion before the last separator occurrence.
    /// Returns null if the separator is not found in the external name.
    /// </summary>
    /// <param name="externalToolName">The external tool name to parse.</param>
    /// <returns>The namespace prefix if found; otherwise, null.</returns>
    public string? ExtractNamespacePrefix(string externalToolName)
    {
        if (string.IsNullOrWhiteSpace(externalToolName))
        {
            return null;
        }

        int separatorIndex = externalToolName.LastIndexOf(Separator, StringComparison.Ordinal);

        if (separatorIndex <= 0)
        {
            // Separator not found or at the start (no prefix)
            return null;
        }

        string potentialPrefix = externalToolName.Substring(0, separatorIndex);

        // Validate that the prefix contains only allowed characters
        try
        {
            ValidateNamespacePrefix(potentialPrefix);
            return potentialPrefix;
        }
        catch
        {
            return null;
        }
    }
}
