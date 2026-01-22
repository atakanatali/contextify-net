using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Contextify.Actions.Abstractions.Models;

/// <summary>
/// Data transfer object representing the context of a tool invocation in the Contextify pipeline.
/// Encapsulates all information needed for action middleware to process, validate, transform,
/// or intercept tool invocations. This struct is immutable by design to ensure thread-safety
/// and prevent accidental modification during pipeline processing.
/// </summary>
public readonly record struct ContextifyInvocationContextDto
{
    /// <summary>
    /// Gets the name of the tool being invoked.
    /// This corresponds to the MCP tool name and is used for routing, filtering, and logging.
    /// Tool names typically follow a naming convention like "namespace:tool_name" or "category.action".
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the arguments passed to the tool invocation as a dictionary of parameter names to values.
    /// The values can be of any type (primitives, objects, arrays) as defined by the tool's schema.
    /// Implementers should deserialize these values based on the tool's expected input schema.
    /// </summary>
    /// <remarks>
    /// The dictionary may contain null values for optional parameters that were not provided.
    /// Complex types are represented as dictionaries or arrays following JSON-like structure.
    /// </remarks>
    [SuppressMessage("Design", "CA1006:Do not nest generic types in member signatures",
        Justification = "Dictionary with object values is necessary for flexible argument passing.")]
    public IReadOnlyDictionary<string, object?> Arguments { get; }

    /// <summary>
    /// Gets the cancellation token for the current invocation.
    /// Actions should respect this token and cooperatively cancel long-running operations when requested.
    /// The token is typically linked to the client's request timeout or connection closure.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the service provider for dependency resolution.
    /// Allows actions to resolve scoped or transient services needed for processing.
    /// Preferred over constructor injection for actions to avoid lifetime management issues.
    /// </summary>
    /// <remarks>
    /// Use GetRequiredService<T> or GetService<T> to resolve services as needed.
    /// Avoid storing references to scoped services beyond the scope of InvokeAsync.
    /// </remarks>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Gets the optional authentication context for propagating auth credentials to downstream services.
    /// Contains bearer tokens, API keys, and custom headers for authenticated tool execution.
    /// Null value indicates no authentication context is available or propagation is not required.
    /// </summary>
    public ContextifyAuthContextDto? AuthContext { get; }

    /// <summary>
    /// Initializes a new instance with the specified tool invocation parameters.
    /// </summary>
    /// <param name="toolName">The name of the tool being invoked. Must not be null or whitespace.</param>
    /// <param name="arguments">The arguments passed to the tool. Must not be null, but can be empty.</param>
    /// <param name="cancellationToken">The cancellation token for the operation.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution. Must not be null.</param>
    /// <exception cref="ArgumentException">Thrown when toolName is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when arguments or serviceProvider is null.</exception>
    [SuppressMessage("Design", "CA1006:Do not nest generic types in member signatures",
        Justification = "Dictionary with object values is necessary for flexible argument passing.")]
    public ContextifyInvocationContextDto(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken,
        IServiceProvider serviceProvider) : this(toolName, arguments, cancellationToken, serviceProvider, null)
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified tool invocation parameters including auth context.
    /// </summary>
    /// <param name="toolName">The name of the tool being invoked. Must not be null or whitespace.</param>
    /// <param name="arguments">The arguments passed to the tool. Must not be null, but can be empty.</param>
    /// <param name="cancellationToken">The cancellation token for the operation.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution. Must not be null.</param>
    /// <param name="authContext">The optional authentication context for downstream propagation.</param>
    /// <exception cref="ArgumentException">Thrown when toolName is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when arguments or serviceProvider is null.</exception>
    [SuppressMessage("Design", "CA1006:Do not nest generic types in member signatures",
        Justification = "Dictionary with object values is necessary for flexible argument passing.")]
    public ContextifyInvocationContextDto(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken,
        IServiceProvider serviceProvider,
        ContextifyAuthContextDto? authContext)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name cannot be null or whitespace.", nameof(toolName));
        }

        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        ToolName = toolName;
        Arguments = arguments;
        CancellationToken = cancellationToken;
        ServiceProvider = serviceProvider;
        AuthContext = authContext;
    }

    /// <summary>
    /// Creates a new invocation context with the same parameters but a different cancellation token.
    /// Useful for chaining actions with modified cancellation behavior.
    /// </summary>
    /// <param name="cancellationToken">The new cancellation token to use.</param>
    /// <returns>A new ContextifyInvocationContextDto with the updated cancellation token.</returns>
    public ContextifyInvocationContextDto WithCancellationToken(CancellationToken cancellationToken)
        => new(ToolName, Arguments, cancellationToken, ServiceProvider, AuthContext);

    /// <summary>
    /// Creates a new invocation context with modified arguments.
    /// Useful for actions that transform or validate input before passing to the next action.
    /// </summary>
    /// <param name="arguments">The new arguments dictionary.</param>
    /// <returns>A new ContextifyInvocationContextDto with the updated arguments.</returns>
    [SuppressMessage("Design", "CA1006:Do not nest generic types in member signatures",
        Justification = "Dictionary with object values is necessary for flexible argument passing.")]
    public ContextifyInvocationContextDto WithArguments(IReadOnlyDictionary<string, object?> arguments)
        => new(ToolName, arguments, CancellationToken, ServiceProvider, AuthContext);

    /// <summary>
    /// Creates a new invocation context with an authentication context for downstream propagation.
    /// Useful for actions that need to inject or modify authentication credentials.
    /// </summary>
    /// <param name="authContext">The authentication context to include.</param>
    /// <returns>A new ContextifyInvocationContextDto with the updated authentication context.</returns>
    public ContextifyInvocationContextDto WithAuthContext(ContextifyAuthContextDto? authContext)
        => new(ToolName, Arguments, CancellationToken, ServiceProvider, authContext);

    /// <summary>
    /// Attempts to retrieve a required service from the service provider.
    /// Throws InvalidOperationException if the service is not registered.
    /// </summary>
    /// <typeparam name="TService">The type of service to retrieve.</typeparam>
    /// <returns>The retrieved service instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the service is not registered.</exception>
    public TService GetRequiredService<TService>()
        where TService : notnull
        => ServiceProvider.GetRequiredService<TService>();

    /// <summary>
    /// Attempts to retrieve a service from the service provider.
    /// Returns null if the service is not registered.
    /// </summary>
    /// <typeparam name="TService">The type of service to retrieve.</typeparam>
    /// <returns>The retrieved service instance, or null if not registered.</returns>
    public TService? GetService<TService>()
        where TService : class
        => ServiceProvider.GetService<TService>();

    /// <summary>
    /// Attempts to get an argument value by name.
    /// </summary>
    /// <param name="argumentName">The name of the argument to retrieve.</param>
    /// <param name="value">The retrieved argument value, or default if not found.</param>
    /// <returns>True if the argument exists; false otherwise.</returns>
    public bool TryGetArgument(string argumentName, out object? value)
        => Arguments.TryGetValue(argumentName, out value);

    /// <summary>
    /// Gets an argument value by name or throws if not present.
    /// </summary>
    /// <param name="argumentName">The name of the argument to retrieve.</param>
    /// <returns>The argument value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the argument does not exist.</exception>
    public object? GetArgument(string argumentName)
        => Arguments.TryGetValue(argumentName, out var value)
            ? value
            : throw new KeyNotFoundException($"Argument '{argumentName}' not found in tool invocation.");
}
