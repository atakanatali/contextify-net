using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;

namespace Contextify.Core.Rules.Catalog;

/// <summary>
/// Data transfer object containing the context for catalog building rules.
/// Encapsulates the state for building a tool catalog snapshot from policies.
/// </summary>
/// <remarks>
/// This DTO maintains the mutable state during catalog building.
/// Rules modify this context to add or skip tools based on validation criteria.
/// </remarks>
public sealed class CatalogBuildContextDto
{
    /// <summary>
    /// Gets the dictionary of validated tools being built.
    /// Key is tool name, value is the tool descriptor.
    /// Rules add tools to this dictionary after validation passes.
    /// </summary>
    public Dictionary<string, ContextifyToolDescriptorEntity> Tools { get; }

    /// <summary>
    /// Gets the policy currently being processed.
    /// Set before rule execution for each policy.
    /// </summary>
    public ContextifyEndpointPolicyDto CurrentPolicy { get; set; }

    /// <summary>
    /// Gets a value indicating whether the current policy was skipped.
    /// Set by validation rules when a policy fails validation.
    /// </summary>
    public bool ShouldSkipPolicy { get; set; }

    /// <summary>
    /// Gets the skip reason when ShouldSkipPolicy is true.
    /// Provides diagnostic information for logging.
    /// </summary>
    public string SkipReason { get; set; }

    /// <summary>
    /// Initializes a new instance with empty tools collection.
    /// </summary>
    /// <param name="capacity">
    /// Initial capacity for the tools dictionary.
    /// Set to expected number of tools to minimize resizes.
    /// </param>
    public CatalogBuildContextDto(int capacity = 16)
    {
        Tools = new Dictionary<string, ContextifyToolDescriptorEntity>(capacity, StringComparer.Ordinal);
        CurrentPolicy = null!;
        ShouldSkipPolicy = false;
        SkipReason = string.Empty;
    }

    /// <summary>
    /// Marks the current policy to be skipped with a specific reason.
    /// </summary>
    /// <param name="reason">The reason for skipping.</param>
    public void SkipPolicy(string reason)
    {
        ShouldSkipPolicy = true;
        SkipReason = reason;
    }

    /// <summary>
    /// Resets skip state for processing the next policy.
    /// </summary>
    public void ResetSkipState()
    {
        ShouldSkipPolicy = false;
        SkipReason = string.Empty;
    }
}
