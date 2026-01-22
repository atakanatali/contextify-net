using Contextify.LoadRunner.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Contextify.LoadRunner;

/// <summary>
/// Entry point for the Contextify Load Runner tool.
/// Executes load tests against MCP endpoints and generates performance reports.
/// </summary>
public static class Program
{
    /// <summary>
    /// Main entry point for the application.
    /// Builds the host and executes the load test based on command-line arguments.
    /// </summary>
    /// <param name="args">Command-line arguments for configuration.</param>
    /// <returns>Exit code for the application (0 for success, non-zero for failure).</returns>
    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        // Register services
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<McpLoadTestService>();
        builder.Services.AddSingleton<ReportWriterService>();

        var host = builder.Build();

        // Parse options from command line or environment
        var options = ParseOptions(args);

        if (options is null)
        {
            PrintUsage();
            return 1;
        }

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Contextify.LoadRunner");
        var loadTestService = host.Services.GetRequiredService<McpLoadTestService>();
        var reportWriter = host.Services.GetRequiredService<ReportWriterService>();

        try
        {
            logger.LogInformation("Contextify Load Runner v1.0.0");
            logger.LogInformation("Target: {Target}", options.FullMcpUrl);
            logger.LogInformation("Concurrency: {Concurrency}, Total Requests: {TotalRequests}",
                options.Concurrency, options.TotalRequests);

            // Execute load test
            var report = await loadTestService.ExecuteLoadTestAsync(options);

            // Write report
            await reportWriter.WriteReportAsync(report, options.ReportPath);

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Load test failed: {Message}", ex.Message);
            return 1;
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Parses command-line arguments into load test options.
    /// Supports both command-line arguments and environment variables.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The parsed options, or null if parsing failed.</returns>
    private static LoadTestOptionsDto? ParseOptions(string[] args)
    {
        var options = new LoadTestOptionsDto
        {
            ServerUrl = GetArgumentValue(args, "--url", "-u") ??
                        Environment.GetEnvironmentVariable("LOADRUNNER_URL") ??
                        "https://localhost:5001",
            McpEndpoint = GetArgumentValue(args, "--endpoint", "-e") ??
                          Environment.GetEnvironmentVariable("LOADRUNNER_ENDPOINT") ??
                          "/mcp",
            Concurrency = int.TryParse(GetArgumentValue(args, "--concurrency", "-c") ??
                                       Environment.GetEnvironmentVariable("LOADRUNNER_CONCURRENCY"),
                                       out var concurrency) ? concurrency : 50,
            TotalRequests = int.TryParse(GetArgumentValue(args, "--requests", "-r") ??
                                        Environment.GetEnvironmentVariable("LOADRUNNER_REQUESTS"),
                                        out var totalRequests) ? totalRequests : 1000,
            RequestTimeoutSeconds = int.TryParse(GetArgumentValue(args, "--timeout", "-t") ??
                                                 Environment.GetEnvironmentVariable("LOADRUNNER_TIMEOUT"),
                                                 out var timeout) ? timeout : 30,
            WarmupDurationSeconds = int.TryParse(GetArgumentValue(args, "--warmup", "-w") ??
                                                  Environment.GetEnvironmentVariable("LOADRUNNER_WARMUP"),
                                                  out var warmup) ? warmup : 5,
            ReportPath = GetArgumentValue(args, "--output", "-o") ??
                        Environment.GetEnvironmentVariable("LOADRUNNER_OUTPUT") ??
                        "artifacts/load-test-report.json",
            ToolName = GetArgumentValue(args, "--tool", null) ??
                      Environment.GetEnvironmentVariable("LOADRUNNER_TOOL"),
            ToolArguments = GetArgumentValue(args, "--args", null) ??
                           Environment.GetEnvironmentVariable("LOADRUNNER_ARGS")
        };

        // Validate required options
        if (args.Length == 0 && Environment.GetEnvironmentVariable("LOADRUNNER_URL") is null)
        {
            // No arguments provided, but that's OK - we'll use defaults
            Console.WriteLine("Using default configuration. Use --help for usage information.");
        }

        if (args.Contains("--help") || args.Contains("-h"))
        {
            return null;
        }

        // Validate numeric ranges
        if (options.Concurrency < 1 || options.Concurrency > 1000)
        {
            Console.WriteLine("Error: Concurrency must be between 1 and 1000");
            return null;
        }

        if (options.TotalRequests < 1)
        {
            Console.WriteLine("Error: Total requests must be at least 1");
            return null;
        }

        if (options.RequestTimeoutSeconds < 1)
        {
            Console.WriteLine("Error: Timeout must be at least 1 second");
            return null;
        }

        return options;
    }

    /// <summary>
    /// Gets an argument value from command-line arguments.
    /// Supports both long and short argument names.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="longName">The long argument name (e.g., "--url").</param>
    /// <param name="shortName">The short argument name (e.g., "-u"), or null if not used.</param>
    /// <returns>The argument value, or null if not found.</returns>
    private static string? GetArgumentValue(string[] args, string longName, string? shortName)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(longName, StringComparison.Ordinal) ||
                (shortName is not null && args[i].Equals(shortName, StringComparison.Ordinal)))
            {
                if (i + 1 < args.Length)
                {
                    return args[i + 1];
                }
                return string.Empty;
            }
        }
        return null;
    }

    /// <summary>
    /// Prints usage information to the console.
    /// Displays all available command-line options and their descriptions.
    /// </summary>
    private static void PrintUsage()
    {
        Console.WriteLine("Contextify Load Runner - Load testing tool for MCP endpoints");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Contextify.LoadRunner [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --url, -u <url>              Server URL (default: https://localhost:5001)");
        Console.WriteLine("  --endpoint, -e <path>        MCP endpoint path (default: /mcp)");
        Console.WriteLine("  --concurrency, -c <number>   Number of concurrent requests (default: 50)");
        Console.WriteLine("  --requests, -r <number>      Total number of requests (default: 1000)");
        Console.WriteLine("  --timeout, -t <seconds>      Request timeout in seconds (default: 30)");
        Console.WriteLine("  --warmup, -w <seconds>       Warmup duration in seconds (default: 5)");
        Console.WriteLine("  --output, -o <path>          Report output path (default: artifacts/load-test-report.json)");
        Console.WriteLine("  --tool <name>                Tool name to test (default: tools/list)");
        Console.WriteLine("  --args <json>                Tool arguments as JSON string");
        Console.WriteLine("  --help, -h                   Show this help message");
        Console.WriteLine();
        Console.WriteLine("Environment Variables:");
        Console.WriteLine("  LOADRUNNER_URL               Server URL");
        Console.WriteLine("  LOADRUNNER_ENDPOINT          MCP endpoint path");
        Console.WriteLine("  LOADRUNNER_CONCURRENCY       Number of concurrent requests");
        Console.WriteLine("  LOADRUNNER_REQUESTS          Total number of requests");
        Console.WriteLine("  LOADRUNNER_TIMEOUT           Request timeout in seconds");
        Console.WriteLine("  LOADRUNNER_WARMUP            Warmup duration in seconds");
        Console.WriteLine("  LOADRUNNER_OUTPUT            Report output path");
        Console.WriteLine("  LOADRUNNER_TOOL              Tool name to test");
        Console.WriteLine("  LOADRUNNER_ARGS              Tool arguments as JSON");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  # Test tools/list with default settings");
        Console.WriteLine("  Contextify.LoadRunner");
        Console.WriteLine();
        Console.WriteLine("  # Test with custom concurrency and requests");
        Console.WriteLine("  Contextify.LoadRunner --url http://localhost:5000 --concurrency 100 --requests 5000");
        Console.WriteLine();
        Console.WriteLine("  # Test a specific tool");
        Console.WriteLine("  Contextify.LoadRunner --tool weather.get_forecast --args '{\"location\":\"NYC\"}'");
    }
}
