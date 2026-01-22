using Contextify.Actions.Abstractions.Models;
using Contextify.Core.Catalog;

namespace Contextify.Core.Execution;

/// <summary>
/// Service interface for executing Contextify tools through their configured endpoints.
/// Provides asynchronous tool invocation with support for various execution modes.
/// Implementations handle HTTP communication, request building, and response parsing.
/// </summary>
public interface IContextifyToolExecutorService
{
    /// <summary>
    /// Executes a tool by invoking its configured endpoint with the provided arguments.
    /// Handles both in-process and remote execution based on the configured execution mode.
    /// Supports JSON and text response formats with proper error handling and cancellation.
    /// </summary>
    /// <param name="toolDescriptor">The descriptor of the tool to execute, containing endpoint and policy information.</param>
    /// <param name="arguments">The arguments to pass to the tool, mapped from parameter names to values.</param>
    /// <param name="authContext">Optional authentication context for propagating security tokens.</param>
    /// <param name="cancellationToken">The cancellation token for cancelling the operation.</param>
    /// <returns>A task representing the asynchronous operation, yielding a tool result with response data or error information.</returns>
    /// <exception cref="ArgumentNullException">Thrown when toolDescriptor is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the tool descriptor has no endpoint configured.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    /// <exception cref="HttpRequestException">Thrown when the HTTP request fails due to network or server issues.</exception>
    /// <exception cref="TimeoutException">Thrown when the tool execution exceeds the configured timeout.</exception>
    /// <remarks>
    /// The execution flow:
    /// 1. Validates the tool descriptor has a configured endpoint
    /// 2. Builds the request URI from the route template and arguments
    /// 3. Builds the request body from arguments if applicable
    /// 4. Makes the HTTP request using the configured execution mode
    /// 5. Parses the response based on content type
    /// 6. Returns a structured result with success/failure information
    ///
    /// For InProcessHttp mode, uses a named HttpClient configured to call the local application.
    /// For RemoteHttp mode (future), will use full network configuration with remote endpoints.
    /// </remarks>
    Task<ContextifyToolResultDto> ExecuteToolAsync(
        ContextifyToolDescriptorEntity toolDescriptor,
        IReadOnlyDictionary<string, object?> arguments,
        ContextifyAuthContextDto? authContext,
        CancellationToken cancellationToken);
}
