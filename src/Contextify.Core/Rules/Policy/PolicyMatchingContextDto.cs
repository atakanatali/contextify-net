using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Policy;

namespace Contextify.Core.Rules.Policy;

/// <summary>
/// Data transfer object containing the context for policy matching rules.
/// Encapsulates all information needed to find a matching policy for an endpoint.
/// </summary>
/// <remarks>
/// This DTO is designed to be lightweight and immutable for efficient rule evaluation.
/// All properties are initialized at construction and never modified.
/// </remarks>
public sealed record PolicyMatchingContextDto
{
    /// <summary>
    /// Gets the endpoint descriptor to match against policies.
    /// Contains route template, HTTP method, operation ID, and display name.
    /// </summary>
    public ContextifyEndpointDescriptor EndpointDescriptor { get; }

    /// <summary>
    /// Gets the collection of policies to search for matches.
    /// Can be whitelist, blacklist, or any other policy collection.
    /// </summary>
    public IReadOnlyList<ContextifyEndpointPolicyDto> Policies { get; }

    /// <summary>
    /// Gets the matching policy found by rule execution.
    /// Set by the first rule that finds a match.
    /// Subsequent rules should check this value to avoid overwriting higher priority matches.
    /// </summary>
    public ContextifyEndpointPolicyDto? MatchedPolicy { get; set; }

    /// <summary>
    /// Initializes a new instance with the required context data.
    /// </summary>
    /// <param name="endpointDescriptor">The endpoint descriptor to match.</param>
    /// <param name="policies">The collection of policies to search.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when endpointDescriptor or policies is null.
    /// </exception>
    public PolicyMatchingContextDto(
        ContextifyEndpointDescriptor endpointDescriptor,
        IReadOnlyList<ContextifyEndpointPolicyDto> policies)
    {
        EndpointDescriptor = endpointDescriptor ?? throw new ArgumentNullException(nameof(endpointDescriptor));
        Policies = policies ?? throw new ArgumentNullException(nameof(policies));
        MatchedPolicy = null;
    }
}
