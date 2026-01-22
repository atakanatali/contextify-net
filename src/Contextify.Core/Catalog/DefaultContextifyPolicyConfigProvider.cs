using System.Threading;
using System.Threading.Tasks;
using Contextify.Config.Abstractions.Policy;
using Microsoft.Extensions.Primitives;

namespace Contextify.Core.Catalog;

/// <summary>
/// Default implementation of <see cref="IContextifyPolicyConfigProvider"/> that returns an empty policy.
/// Used as a fallback when no specific policy provider is registered.
/// </summary>
public sealed class DefaultContextifyPolicyConfigProvider : IContextifyPolicyConfigProvider
{
    private static readonly ValueTask<ContextifyPolicyConfigDto> EmptyPolicyContent = 
        new(new ContextifyPolicyConfigDto
        {
            SourceVersion = "0",
            SchemaVersion = 1,
            Whitelist = Array.Empty<ContextifyEndpointPolicyDto>()
        });

    public ValueTask<ContextifyPolicyConfigDto> GetAsync(CancellationToken ct)
    {
        return EmptyPolicyContent;
    }

    public IChangeToken? Watch()
    {
        return null;
    }
}
