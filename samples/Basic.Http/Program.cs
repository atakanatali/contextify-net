using Contextify.AspNetCore.Extensions;
using Contextify.Core.Builder;
using Contextify.Core.Extensions;
using Contextify.Transport.Http.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Register Contextify with HTTP transport and Endpoint Discovery
builder.Services.AddContextify()
    .ConfigureHttp()
    .AddEndpointDiscovery();

var app = builder.Build();

// Define a simple hello tool
app.MapGet("/hello", (string name) => new { message = $"Hello, {name}!" })
   .WithName("hello")
   .WithDisplayName("Hello Tool")
   .WithDescription("Say hello to someone")
   .Produces<object>(200, "application/json");

// Map contextify endpoints
app.MapMcpEndpoints();

app.Run();
