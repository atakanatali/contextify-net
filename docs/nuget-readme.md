# Contextify

A modular, enterprise-grade .NET framework for Model Context Protocol (MCP) server and tool management. Expose your application's capabilities to AI assistants through a standardized interface.

## Installation

### For HTTP API (Web API / Microservices)

```bash
dotnet add package Contextify.AspNetCore
dotnet add package Contextify.Transport.Http
```

### For STDIO (CLI Tools / Local Development)

```bash
dotnet add package Contextify.AspNetCore
dotnet add package Contextify.Transport.Stdio
```

### For Gateway (Multi-Backend Aggregation)

```bash
dotnet add package Contextify.Gateway.Core
dotnet add package Contextify.Gateway.Discovery.Consul
```

### Official MCP SDK Support

```bash
dotnet add package Contextify.Mcp.OfficialAdapter
```

### Configuration Providers

```bash
dotnet add package Contextify.Config.AppSettings
dotnet add package Contextify.Config.Consul
```

### Additional Features

```bash
dotnet add package Contextify.OpenApi
dotnet add package Contextify.Actions.Defaults
```

## Quick Start

### Program.cs

```csharp
using Contextify.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add Contextify with configuration
builder.Services.AddContextify()
    .AddHttpTransport()
    .AddAppSettingsPolicyProvider();

var app = builder.Build();

// Map MCP endpoint
app.MapContextifyMcp();

app.Run();
```

### appsettings.json

```json
{
  "Contextify": {
    "Security": {
      "DefaultPolicy": "Deny",
      "AllowedTools": ["weather:*", "db:read:*"],
      "BlockedTools": ["db:delete:*"]
    }
  }
}
```

## Documentation

- [Full Documentation](https://github.com/atakanatali/contextify-net)
- [Architecture Guide](https://github.com/atakanatali/contextify-net/blob/main/docs/architecture.md)
- [Official MCP Specification](https://modelcontextprotocol.io/)

## License

MIT
