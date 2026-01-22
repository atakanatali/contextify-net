using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Contextify.Transport.Http.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;

namespace Contextify.Transport.Http.Middleware;

/// <summary>
/// Middleware that enforces request body size limits for MCP JSON-RPC endpoints.
/// Provides structured JSON-RPC error responses for oversized requests.
/// Works in conjunction with Kestrel's MaxRequestBodySize for defense in depth.
/// </summary>
public sealed class ContextifyRequestSizeLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ContextifyRequestSizeLimitMiddleware> _logger;
    private readonly ContextifyHttpOptions _options;
    private const string JsonContentType = "application/json";

    /// <summary>
    /// Initializes a new instance with the next middleware delegate and dependencies.
    /// </summary>
    /// <param name="next">The next middleware delegate in the pipeline.</param>
    /// <param name="options">The HTTP transport security options.</param>
    /// <param name="logger">The logger for diagnostics and audit trail.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public ContextifyRequestSizeLimitMiddleware(
        RequestDelegate next,
        IOptions<ContextifyHttpOptions> options,
        ILogger<ContextifyRequestSizeLimitMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Processes the HTTP request and enforces size limits before passing to the next middleware.
    /// Returns a structured JSON-RPC error response for requests exceeding size limits.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // Get the content length header if available
        var contentLength = context.Request.ContentLength;

        // Check if the content length exceeds the limit (early rejection)
        if (contentLength.HasValue && contentLength.Value > _options.MaxRequestBodyBytes)
        {
            _logger.LogWarning(
                "Request size limit exceeded: Content-Length {ContentLength} bytes exceeds maximum {MaxSize} bytes for path {Path}",
                contentLength.Value,
                _options.MaxRequestBodyBytes,
                context.Request.Path);

            await WriteSizeLimitErrorResponseAsync(context).ConfigureAwait(false);
            return;
        }

        // Enable request buffering to allow us to check the actual body size
        // This is necessary because Content-Length can be spoofed or missing
        context.Request.EnableBuffering();

        // Read the body into a buffer to check actual size
        var originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();

        try
        {
            await context.Request.Body.CopyToAsync(memoryStream, context.RequestAborted).ConfigureAwait(false);

            // Check actual body size
            if (memoryStream.Length > _options.MaxRequestBodyBytes)
            {
                _logger.LogWarning(
                    "Request size limit exceeded: Actual body size {ActualSize} bytes exceeds maximum {MaxSize} bytes for path {Path}",
                    memoryStream.Length,
                    _options.MaxRequestBodyBytes,
                    context.Request.Path);

                await WriteSizeLimitErrorResponseAsync(context).ConfigureAwait(false);
                return;
            }

            // Reset the body position for the next middleware to read
            memoryStream.Position = 0;
            context.Request.Body = memoryStream;

            // Call the next middleware
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            // Restore the original body stream
            context.Request.Body = originalBodyStream;
        }
    }

    /// <summary>
    /// Writes a structured JSON-RPC error response for requests exceeding size limits.
    /// The response includes a correlation ID for debugging and follows JSON-RPC 2.0 format.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task WriteSizeLimitErrorResponseAsync(HttpContext context)
    {
        context.Response.ContentType = JsonContentType;
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;

        var correlationId = _options.IncludeCorrelationIdInErrors
            ? Guid.NewGuid().ToString("N")
            : null;

        var errorResponse = new
        {
            jsonrpc = "2.0",
            id = (object?)null,
            error = new
            {
                code = _options.SizeLimitErrorCode,
                message = $"Request body size exceeds maximum allowed size of {_options.MaxRequestBodyBytes} bytes.",
                data = correlationId
            }
        };

        // Log the correlation ID for server-side debugging
        if (correlationId is not null)
        {
            _logger.LogInformation(
                "Size limit error generated with correlation ID {CorrelationId} for path {Path}",
                correlationId,
                context.Request.Path);
        }

        await context.Response.WriteAsJsonAsync(
            errorResponse,
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }
}

/// <summary>
/// Extension methods for registering the request size limit middleware in the ASP.NET Core pipeline.
/// </summary>
public static class ContextifyRequestSizeLimitMiddlewareExtensions
{
    /// <summary>
    /// Adds the Contextify request size limit middleware to the application pipeline.
    /// Enforces request body size limits with structured JSON-RPC error responses.
    /// </summary>
    /// <param name="builder">The application builder to configure.</param>
    /// <returns>The application builder for fluent chaining.</returns>
    /// <remarks>
    /// This middleware should be added early in the pipeline, before endpoint routing,
    /// to efficiently reject oversized requests before expensive processing.
    ///
    /// Usage:
    /// <code>
    /// var app = builder.Build();
    /// app.UseContextifyRequestSizeLimit();
    /// app.MapContextifyMcp("/mcp");
    /// </code>
    /// </remarks>
    public static IApplicationBuilder UseContextifyRequestSizeLimit(this IApplicationBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.UseMiddleware<ContextifyRequestSizeLimitMiddleware>();
    }
}
