using Contextify.Config.Consul.Extensions;
using Contextify.Core.Builder;
using Contextify.Core.Extensions;
using Contextify.Transport.Http.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Register Contextify with HTTP transport and Consul Policy Provider
builder.Services.AddContextify()
    .ConfigureHttp();

// Register Consul Policy Provider
// In a real scenario, these values would come from environment variables or appsettings
builder.Services.AddConsulPolicyProvider(options =>
{
    options.Address = builder.Configuration["Consul:Address"] ?? "http://localhost:8500";
    options.KeyPath = builder.Configuration["Consul:KeyPath"] ?? "contextify/policy/config";
    options.Token = builder.Configuration["Consul:Token"]; // Optional ACL token
    options.Datacenter = builder.Configuration["Consul:Datacenter"]; // Optional DC
    options.MinReloadIntervalMs = 5000; // Poll every 5 seconds
});

var app = builder.Build();

app.MapGet("/", () => "Contextify Consul Sample - MCP endpoint at /mcp");

// Map MCP endpoints
app.MapMcpEndpoints("/mcp");

app.Run();
