using Contextify.Core.Builder;
using Contextify.Core.Extensions;
using Contextify.Transport.Stdio.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Register Contextify with STDIO transport
builder.Services.AddContextify(options => 
    {
        builder.Configuration.GetSection("Contextify").Bind(options);
    })
    .ConfigureStdio();

var host = builder.Build();

await host.RunAsync();
