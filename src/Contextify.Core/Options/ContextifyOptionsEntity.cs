using Contextify.Core.Options;
using Contextify.Core.Redaction;

namespace Contextify.Core;

/// <summary>
/// Root configuration container for all Contextify framework settings.
/// Aggregates logging, policy, actions, and transport configuration into a single options entity.
/// Provides centralized configuration management for the entire Contextify runtime.
/// </summary>
public sealed class ContextifyOptionsEntity
{
    /// <summary>
    /// Gets or sets the transport mode for MCP communication.
    /// Determines how the Contextify runtime communicates with MCP clients and servers.
    /// Default value is Auto, which selects the appropriate transport based on hosting environment.
    /// </summary>
    public ContextifyTransportMode TransportMode { get; set; } = ContextifyTransportMode.Auto;

    /// <summary>
    /// Gets or sets the logging configuration options.
    /// Controls verbosity, scopes, and categories for Contextify-specific logging.
    /// When null, default logging settings are applied.
    /// </summary>
    public ContextifyLoggingOptionsEntity? Logging { get; set; }

    /// <summary>
    /// Gets or sets the security and access policy options.
    /// Defines allowlists, denylists, and default access control behavior for tools and resources.
    /// Implements deny-by-default security for production-safe defaults.
    /// When null, default secure policy settings are applied.
    /// </summary>
    public ContextifyPolicyOptionsEntity? Policy { get; set; }

    /// <summary>
    /// Gets or sets the action processing and execution options.
    /// Configures middleware, validation, rate limiting, caching, and retry behavior for actions.
    /// When null, default action processing settings are applied.
    /// </summary>
    public ContextifyActionsOptionsEntity? Actions { get; set; }

    /// <summary>
    /// Gets or sets the redaction options for sensitive information in tool outputs.
    /// Configures field-name based JSON redaction and optional pattern-based text redaction.
    /// When null, default redaction settings (disabled) are applied.
    /// </summary>
    public ContextifyRedactionOptionsEntity? Redaction { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Contextify runtime is enabled.
    /// When false, the MCP runtime is not started even if configured.
    /// Useful for feature flags or multi-environment deployments.
    /// Default value is true.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the application name reported to MCP clients.
    /// Used for identification in protocol handshakes and logging.
    /// When null, defaults to the entry assembly name.
    /// </summary>
    public string? ApplicationName { get; set; }

    /// <summary>
    /// Gets or sets the application version reported to MCP clients.
    /// Used for version negotiation and compatibility checks.
    /// When null, defaults to the entry assembly version.
    /// </summary>
    public string? ApplicationVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to expose debug endpoints.
    /// When enabled, provides additional diagnostics and introspection capabilities.
    /// Should be disabled in production environments for security.
    /// Default value is false.
    /// </summary>
    public bool EnableDebugEndpoints { get; set; }

    /// <summary>
    /// Initializes a new instance with default Contextify configuration values.
    /// All nested options are initialized to their defaults for immediate usability.
    /// </summary>
    public ContextifyOptionsEntity()
    {
        TransportMode = ContextifyTransportMode.Auto;
        Logging = new ContextifyLoggingOptionsEntity();
        Policy = new ContextifyPolicyOptionsEntity();
        Actions = new ContextifyActionsOptionsEntity();
        Redaction = new ContextifyRedactionOptionsEntity();
        IsEnabled = true;
        ApplicationName = null;
        ApplicationVersion = null;
        EnableDebugEndpoints = false;
    }

    /// <summary>
    /// Validates the current configuration and ensures all settings are within acceptable ranges.
    /// Throws an InvalidOperationException if validation fails.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (Actions is not null)
        {
            if (Actions.DefaultExecutionTimeoutSeconds <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(Actions.DefaultExecutionTimeoutSeconds)} must be greater than zero. " +
                    $"Provided value: {Actions.DefaultExecutionTimeoutSeconds}");
            }

            if (Actions.MaxConcurrentActions <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(Actions.MaxConcurrentActions)} must be greater than zero. " +
                    $"Provided value: {Actions.MaxConcurrentActions}");
            }

            if (Actions.MaxQueueDepth <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(Actions.MaxQueueDepth)} must be greater than zero. " +
                    $"Provided value: {Actions.MaxQueueDepth}");
            }

            if (Actions.MaxRetryAttempts < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(Actions.MaxRetryAttempts)} cannot be negative. " +
                    $"Provided value: {Actions.MaxRetryAttempts}");
            }

            if (Actions.RetryDelayMilliseconds < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(Actions.RetryDelayMilliseconds)} cannot be negative. " +
                    $"Provided value: {Actions.RetryDelayMilliseconds}");
            }
        }
    }

    /// <summary>
    /// Creates a deep copy of the current options instance.
    /// Useful for creating modified snapshots without affecting the original configuration.
    /// </summary>
    /// <returns>A new ContextifyOptionsEntity instance with copied values.</returns>
    public ContextifyOptionsEntity Clone()
    {
        var clone = new ContextifyOptionsEntity
        {
            TransportMode = TransportMode,
            IsEnabled = IsEnabled,
            ApplicationName = ApplicationName,
            ApplicationVersion = ApplicationVersion,
            EnableDebugEndpoints = EnableDebugEndpoints
        };

        if (Logging is not null)
        {
            clone.Logging = new ContextifyLoggingOptionsEntity
            {
                EnableDetailedLogging = Logging.EnableDetailedLogging,
                LogIncomingRequests = Logging.LogIncomingRequests,
                LogOutgoingResponses = Logging.LogOutgoingResponses,
                LogToolInvocations = Logging.LogToolInvocations,
                LogTransportEvents = Logging.LogTransportEvents,
                MinimumLogLevel = Logging.MinimumLogLevel,
                IncludeScopes = Logging.IncludeScopes
            };
        }

        if (Policy is not null)
        {
            clone.Policy = new ContextifyPolicyOptionsEntity
            {
                AllowByDefault = Policy.AllowByDefault,
                AllowedTools = new HashSet<string>(Policy.AllowedTools),
                DeniedTools = new HashSet<string>(Policy.DeniedTools),
                AllowedNamespaces = new HashSet<string>(Policy.AllowedNamespaces),
                AllowedResources = new HashSet<string>(Policy.AllowedResources),
                DeniedResources = new HashSet<string>(Policy.DeniedResources),
                DenyOnPolicyEvaluationFailure = Policy.DenyOnPolicyEvaluationFailure,
                EnableRateLimiting = Policy.EnableRateLimiting,
                ValidateArguments = Policy.ValidateArguments
            };
        }

        if (Actions is not null)
        {
            clone.Actions = new ContextifyActionsOptionsEntity
            {
                EnableDefaultMiddleware = Actions.EnableDefaultMiddleware,
                EnableValidation = Actions.EnableValidation,
                EnableCaching = Actions.EnableCaching,
                DefaultExecutionTimeoutSeconds = Actions.DefaultExecutionTimeoutSeconds,
                MaxConcurrentActions = Actions.MaxConcurrentActions,
                RejectWhenOverCapacity = Actions.RejectWhenOverCapacity,
                MaxQueueDepth = Actions.MaxQueueDepth,
                EnableRetry = Actions.EnableRetry,
                MaxRetryAttempts = Actions.MaxRetryAttempts,
                RetryDelayMilliseconds = Actions.RetryDelayMilliseconds,
                EnableMetrics = Actions.EnableMetrics,
                EnableTracing = Actions.EnableTracing
            };
        }

        if (Redaction is not null)
        {
            clone.Redaction = new ContextifyRedactionOptionsEntity(
                Redaction.Enabled,
                Redaction.RedactJsonFields,
                Redaction.RedactPatterns);
        }

        return clone;
    }
}
