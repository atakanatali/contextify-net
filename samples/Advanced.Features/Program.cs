using Contextify.Core;
using Contextify.Core.Builder;
using Contextify.Core.Extensions;
using Contextify.Transport.Http.Extensions;
using Contextify.OpenApi.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Register Contextify with Advanced Features
builder.Services.AddContextify()
    .ConfigureHttp()
    .ConfigureOpenApiEnrichment("v1") // Use the correct extension method
    .Configure(options => // Configure options using the central Configure method
    {
        // Policy Configuration
        if (options.Policy != null)
        {
            options.Policy.AllowByDefault = false; // Secure by default
            options.Policy.AllowedResources.Add("file:///*"); // Example whitelist
        }

        if (options.Logging != null)
        {
            options.Logging.EnableDetailedLogging = true;
        }
        
        // Example: Application Metadata
        options.ApplicationName = "Advanced MCP Server";
        options.ApplicationVersion = "1.0.0-beta";
    });

// Add standard ASP.NET Core services for Swagger UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Enable Swagger UI for visualization
app.UseSwagger();
app.UseSwaggerUI();

// Map the MCP JSON-RPC endpoint
app.MapMcpEndpoints();

app.Run();
