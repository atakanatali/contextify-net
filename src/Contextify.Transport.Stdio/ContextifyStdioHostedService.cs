using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Contextify.Core;
using Contextify.Core.Options;
using Contextify.Transport.Stdio.JsonRpc;
using Contextify.Transport.Stdio.JsonRpc.Dto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Contextify.Transport.Stdio;

/// <summary>
/// Background service that implements MCP server over STDIO transport.
/// Reads JSON-RPC requests from standard input and writes responses to standard output.
/// Uses a single-threaded message loop to preserve ordering and reduce contention.
/// </summary>
public sealed class ContextifyStdioHostedService : BackgroundService
{
    private const int DefaultBufferSize = 8192;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    private readonly IContextifyStdioJsonRpcHandler _jsonRpcHandler;
    private readonly ILogger<ContextifyStdioHostedService> _logger;
    private readonly ContextifyOptionsEntity _options;
    private readonly TextReader? _input;
    private readonly TextWriter? _output;

    /// <summary>
    /// Initializes a new instance with injected dependencies and real console streams.
    /// Uses Console.In for input and Console.Out for output.
    /// </summary>
    /// <param name="jsonRpcHandler">The JSON-RPC request handler.</param>
    /// <param name="options">The Contextify options.</param>
    /// <param name="logger">The logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public ContextifyStdioHostedService(
        IContextifyStdioJsonRpcHandler jsonRpcHandler,
        ContextifyOptionsEntity options,
        ILogger<ContextifyStdioHostedService> logger)
        : this(jsonRpcHandler, options, logger, Console.In, Console.Out)
    {
    }

    /// <summary>
    /// Initializes a new instance with injected dependencies and custom streams.
    /// Allows dependency injection of input/output streams for testing scenarios.
    /// </summary>
    /// <param name="jsonRpcHandler">The JSON-RPC request handler.</param>
    /// <param name="options">The Contextify options.</param>
    /// <param name="logger">The logger for diagnostics.</param>
    /// <param name="input">The text reader for input (null for Console.In).</param>
    /// <param name="output">The text writer for output (null for Console.Out).</param>
    /// <exception cref="ArgumentNullException">Thrown when jsonRpcHandler or logger is null.</exception>
    public ContextifyStdioHostedService(
        IContextifyStdioJsonRpcHandler jsonRpcHandler,
        ContextifyOptionsEntity options,
        ILogger<ContextifyStdioHostedService> logger,
        TextReader? input,
        TextWriter? output)
    {
        _jsonRpcHandler = jsonRpcHandler ?? throw new ArgumentNullException(nameof(jsonRpcHandler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _input = input;
        _output = output;

        _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }


    /// <summary>
    /// Executes the main message loop for processing STDIO JSON-RPC requests.
    /// Reads lines from input, processes them as JSON-RPC requests, and writes responses.
    /// Continues until cancellation is requested or input stream ends.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for stopping the service.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check if STDIO transport should be enabled
        bool shouldEnableStdio = _options.TransportMode switch
        {
            ContextifyTransportMode.Stdio => true,
            ContextifyTransportMode.Both => true,
            ContextifyTransportMode.Auto => !IsWebHostEnvironment(),
            ContextifyTransportMode.Http => false,
            _ => false
        };

        if (!shouldEnableStdio)
        {
            _logger.LogInformation("Contextify STDIO transport is disabled based on TransportMode ({TransportMode})", _options.TransportMode);
            return;
        }

        _logger.LogInformation("Contextify STDIO transport starting (Mode: {TransportMode})", _options.TransportMode);

        var reader = _input ?? Console.In;
        var writer = _output ?? Console.Out;

        try
        {
            await ProcessMessageLoopAsync(reader, writer, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Contextify STDIO transport stopped via cancellation");
        }
        finally
        {
            _logger.LogInformation("Contextify STDIO transport shut down");
        }
    }

    private static bool IsWebHostEnvironment()
    {
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
        if (entryAssembly is not null)
        {
            var referencedAssemblies = entryAssembly.GetReferencedAssemblies();
            return referencedAssemblies.Any(a => a?.Name?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ?? false);
        }

        return false;
    }


    /// <summary>
    /// Processes the message loop for reading and handling JSON-RPC requests.
    /// Uses a single-threaded loop to ensure message ordering and minimize contention.
    /// </summary>
    /// <param name="reader">The text reader for input.</param>
    /// <param name="writer">The text writer for output.</param>
    /// <param name="stoppingToken">Cancellation token for stopping the loop.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessMessageLoopAsync(
        TextReader reader,
        TextWriter writer,
        CancellationToken stoppingToken)
    {
        var stopwatch = new Stopwatch();
        var messageCount = 0L;

        _logger.LogDebug("Starting STDIO message loop");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Read a line asynchronously from input
            var line = await ReadLineAsync(reader, stoppingToken).ConfigureAwait(false);

            if (line is null)
            {
                // End of stream - normal shutdown
                _logger.LogInformation("STDIO input stream closed");
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                // Skip empty lines
                continue;
            }

            messageCount++;
            stopwatch.Restart();

            try
            {
                // Process the request and get response
                var response = await ProcessRequestAsync(line, stoppingToken).ConfigureAwait(false);

                // Write response to output
                if (response is not null)
                {
                    await WriteResponseAsync(writer, response, stoppingToken).ConfigureAwait(false);
                }

                stopwatch.Stop();
                _logger.LogDebug(
                    "Processed message #{MessageCount} in {DurationMs}ms",
                    messageCount,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug("Message processing cancelled");
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "Error processing message #{MessageCount} after {DurationMs}ms: {ErrorMessage}",
                    messageCount,
                    stopwatch.ElapsedMilliseconds,
                    ex.Message);

                // Try to write error response
                try
                {
                    var errorResponse = JsonRpcResponseDto.CreateError(
                        -32603, // Internal error
                        $"Internal server error: {ex.Message}",
                        null);
                    await WriteResponseAsync(writer, errorResponse, stoppingToken).ConfigureAwait(false);
                }
                catch
                {
                    // If we can't write the error response, the connection is likely broken
                    break;
                }
            }
        }

        _logger.LogInformation("STDIO message loop ended. Total messages processed: {MessageCount}", messageCount);
    }

    /// <summary>
    /// Reads a single line from the input reader asynchronously.
    /// Handles cancellation cleanly without throwing exceptions.
    /// </summary>
    /// <param name="reader">The text reader to read from.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the read line, or null if end of stream.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<string?> ReadLineAsync(TextReader reader, CancellationToken cancellationToken)
    {
        try
        {
            // For StreamReader, we can use ReadLineAsync with cancellation
            // For TextReader base class, we need to use a different approach
            if (reader is StreamReader streamReader)
            {
                return await streamReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }

            // Fallback for TextReader - run synchronously but check cancellation
            // This is less ideal but maintains compatibility
            return reader.ReadLine();
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Read operation cancelled");
            return null;
        }
    }

    /// <summary>
    /// Processes a single JSON-RPC request and returns the response.
    /// Deserializes the request, routes to handler, and serializes the response.
    /// </summary>
    /// <param name="requestJson">The raw JSON request string.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task yielding the JSON-RPC response, or null for notifications.</returns>
    private async Task<JsonRpcResponseDto?> ProcessRequestAsync(
        string requestJson,
        CancellationToken cancellationToken)
    {
        // Deserialize the request
        JsonRpcRequestDto? request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequestDto>(requestJson, _jsonSerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Failed to deserialize JSON-RPC request: {ErrorMessage}", ex.Message);
            return JsonRpcResponseDto.CreateError(
                -32700, // Parse error
                "Parse error: Invalid JSON",
                null);
        }

        if (request is null)
        {
            _logger.LogWarning("Deserialized request is null");
            return JsonRpcResponseDto.CreateError(
                -32600, // Invalid request
                "Invalid request",
                null);
        }

        // Process the request through the handler
        return await _jsonRpcHandler.HandleRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a JSON-RPC response to the output writer.
    /// Serializes the response and writes it with a newline terminator.
    /// </summary>
    /// <param name="writer">The text writer to write to.</param>
    /// <param name="response">The JSON-RPC response to write.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task WriteResponseAsync(
        TextWriter writer,
        JsonRpcResponseDto response,
        CancellationToken cancellationToken)
    {
        var responseJson = JsonSerializer.Serialize(response, _jsonSerializerOptions);
        await writer.WriteLineAsync(responseJson).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }
}
