using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Rules;
using Contextify.Core.Rules.Catalog;
using Microsoft.Extensions.Logging;

namespace Contextify.Core.Catalog;

/// <summary>
/// Service for building tool catalog snapshots from policy configurations using a rule engine.
/// Provides extensible validation through rule-based architecture.
/// </summary>
/// <remarks>
/// This service uses the rule engine pattern to execute catalog building rules.
/// Each rule implements a specific validation strategy (enabled check, tool name validation, duplicate detection).
/// Rules execute in priority order, with validation failures preventing tool inclusion.
///
/// Thread-safety: This service is thread-safe after construction.
/// The same instance can be safely used across multiple threads concurrently.
/// </remarks>
public sealed class ContextifyCatalogBuilderService
{
    /// <summary>
    /// The rule engine executor for catalog building.
    /// Pre-configured with all validation rules sorted by priority.
    /// </summary>
    private readonly IRuleEngineExecutor<CatalogBuildContextDto> _ruleExecutor;

    /// <summary>
    /// The logger for diagnostic output.
    /// </summary>
    private readonly ILogger<ContextifyCatalogBuilderService>? _logger;

    /// <summary>
    /// Initializes a new instance with the specified logging support.
    /// Configures the rule engine with all catalog building rules.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <remarks>
    /// The rule engine is configured with three validation rules in priority order:
    /// 1. CatalogEnabledPolicyValidationRule (highest priority) - skips disabled policies
    /// 2. CatalogToolNameValidationRule (medium priority) - validates tool name presence
    /// 3. CatalogDuplicateToolDetectionRule (lowest priority) - prevents duplicate tools
    ///
    /// Each rule checks the skip status before executing, ensuring that once a policy
    /// is marked for skipping, subsequent validation rules don't override the decision.
    /// </remarks>
    public ContextifyCatalogBuilderService(ILogger<ContextifyCatalogBuilderService>? logger = null)
    {
        _logger = logger;

        var rules = new IRule<CatalogBuildContextDto>[]
        {
            new CatalogEnabledPolicyValidationRule(null),
            new CatalogToolNameValidationRule(null),
            new CatalogDuplicateToolDetectionRule(null)
        };

        _ruleExecutor = new RuleEngineExecutor<CatalogBuildContextDto>(rules, null);
    }

    /// <summary>
    /// Builds a new catalog snapshot from the provided policy configuration.
    /// Extracts tools from the policy whitelist and constructs tool descriptors.
    /// </summary>
    /// <param name="policyConfig">The policy configuration to build from.</param>
    /// <returns>A new catalog snapshot containing tools from the policy.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when policyConfig is null.
    /// </exception>
    /// <remarks>
    /// This method processes each policy in the whitelist through the rule engine:
    /// <code>
    /// For each policy in whitelist:
    ///     1. Create context with policy
    ///     2. Execute validation rules
    ///     3. If not skipped, add tool to catalog
    ///     4. Reset skip state for next policy
    /// </code>
    ///
    /// The rule engine ensures that:
    /// - Disabled policies are skipped immediately
    /// - Policies without tool names are skipped
    /// - Duplicate tool names are detected and skipped
    /// - First occurrence of a tool is preserved
    /// </remarks>
    public async Task<ContextifyToolCatalogSnapshotEntity> BuildSnapshotFromPolicyAsync(
        ContextifyPolicyConfigDto policyConfig)
    {
        if (policyConfig is null)
        {
            throw new ArgumentNullException(nameof(policyConfig));
        }

        var context = new CatalogBuildContextDto(capacity: policyConfig.Whitelist.Count);

        foreach (var policy in policyConfig.Whitelist)
        {
            context.CurrentPolicy = policy;
            context.ResetSkipState();

            await _ruleExecutor.ExecuteAsync(context, CancellationToken.None).ConfigureAwait(false);

            if (context.ShouldSkipPolicy)
            {
                _logger?.LogTrace(
                    "Skipping policy: {Reason} (ToolName: '{ToolName}', OperationId: '{OperationId}')",
                    context.SkipReason,
                    policy.ToolName ?? "(none)",
                    policy.OperationId ?? "(none)");
                continue;
            }

            // All validations passed - create the tool descriptor
            var toolName = policy.ToolName!;

            var endpointDescriptor = new ContextifyEndpointDescriptorEntity(
                routeTemplate: policy.RouteTemplate,
                httpMethod: policy.HttpMethod,
                operationId: policy.OperationId,
                displayName: policy.DisplayName,
                produces: [],
                consumes: [],
                requiresAuth: policy.AuthPropagationMode != Config.Abstractions.Policy.ContextifyAuthPropagationMode.None);

            var toolDescriptor = new ContextifyToolDescriptorEntity(
                toolName: toolName,
                description: policy.Description,
                inputSchemaJson: null,
                endpointDescriptor: endpointDescriptor,
                effectivePolicy: policy);

            context.Tools[toolName] = toolDescriptor;

            _logger?.LogTrace(
                "Added tool '{ToolName}' to catalog (OperationId: '{OperationId}')",
                toolName,
                policy.OperationId ?? "(none)");
        }

        var snapshot = new ContextifyToolCatalogSnapshotEntity(
            createdUtc: DateTime.UtcNow,
            policySourceVersion: policyConfig.SourceVersion,
            toolsByName: context.Tools);

        _logger?.LogDebug(
            "Built snapshot with {ToolCount} tools from policy configuration. Source version: {SourceVersion}",
            snapshot.ToolCount,
            snapshot.PolicySourceVersion ?? "none");

        return snapshot;
    }

    /// <summary>
    /// Synchronously builds a new catalog snapshot from the provided policy configuration.
    /// Convenience method for scenarios where async is not required.
    /// </summary>
    /// <param name="policyConfig">The policy configuration to build from.</param>
    /// <returns>A new catalog snapshot containing tools from the policy.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when policyConfig is null.
    /// </exception>
    /// <remarks>
    /// This method wraps the async implementation and blocks on the result.
    /// For high-throughput scenarios, prefer the async version.
    /// </remarks>
    public ContextifyToolCatalogSnapshotEntity BuildSnapshotFromPolicy(
        ContextifyPolicyConfigDto policyConfig)
    {
        // Async operation is fast (no I/O), so blocking is acceptable here
        // Using GetAwaiter().GetResult() to preserve exception unwinding
        return BuildSnapshotFromPolicyAsync(policyConfig).GetAwaiter().GetResult();
    }
}
