using Contextify.Core;
using Contextify.Core.Builder;
using Contextify.Core.Extensions;
using Contextify.Transport.Http.Extensions;
using Contextify.Core.Options;

using Contextify.Config.AppSettings.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// 1. Register Contextify with HTTP transport
builder.Services.AddContextify()
    .ConfigureHttp();

// 2. Bind general Contextify options (Logging, etc.) from configuration
// This allows controlling things like EnableDetailedLogging via appsettings.json
builder.Services.Configure<ContextifyOptionsEntity>(builder.Configuration.GetSection("Contextify"));

// 3. Register specialized AppSettings Policy Provider
// This enables dynamic policy reloading when appsettings.json changes
builder.Services.AddAppSettingsPolicyProvider(builder.Configuration);

var app = builder.Build();

app.MapMcpEndpoints();

app.Run();
