# Contextify

Contextify is a modular, enterprise-grade .NET framework for the **Model Context Protocol (MCP)**. It allows developers to build securely orchestrated MCP servers that expose tools, resources, and prompts to AI assistants.

Built upon the official [Anthropic ModelContextProtocol SDK](https://www.nuget.org/packages/ModelContextProtocol), Contextify provides the infrastructure needed for production-ready deployments.

## Package List

| Area | Package |
|------|---------|
| **Core** | `Contextify.Abstractions`, `Contextify.Core`, `Contextify.Mcp.OfficialAdapter` |
| **Integration** | `Contextify.AspNetCore` |
| **Transports** | `Contextify.Transport.Http`, `Contextify.Transport.Stdio` |
| **Tools** | `Contextify.OpenApi`, `Contextify.Actions.Defaults` |
| **Config** | `Contextify.Config.AppSettings`, `Contextify.Config.Consul` |
| **Gateway** | `Contextify.Gateway.Core`, `Contextify.Gateway.Discovery.Consul` |

## Basic Usage (HTTP)

1. **Install Packages**
   ```bash
   dotnet add package Contextify.AspNetCore
   dotnet add package Contextify.Transport.Http
   ```

2. **Configure Services**
   ```csharp
   // Program.cs
   builder.Services.AddContextify()
       .AddHttpTransport()
       .AddAppSettingsPolicyProvider();

   var app = builder.Build();
   app.MapContextifyMcp();
   app.Run();
   ```

3. **Whitelist Tools (Security)**
   In `appsettings.json`:
   ```json
   {
     "Contextify": {
       "Security": {
         "Whitelist": [ { "ToolName": "my-tool:*", "Enabled": true } ]
       }
     }
   }
   ```

## Key Features

- **Deny-by-Default Security**: Full control over which tools are visible to the AI.
- **Transports**: Native support for HTTP (SSE) and Standard I/O (Stdio).
- **OpenAPI Integration**: Convert any Swagger/OpenAPI spec to MCP tools instantly.
- **Gateway Aggregation**: Combine multiple MCP servers into a single hub.
- **Dynamic Policy**: Change permissions at runtime via Consul.

## Documentation & Support

Full documentation, architecture guides, and examples are available at:
[https://github.com/atakanatali/contextify-net](https://github.com/atakanatali/contextify-net)
