# Contextify

<p align="center">
  <img width="256" height="256" alt="Contextify Logo" src="./icon.png" />
</p>

<p align="center">
  <strong>A modular, enterprise-grade .NET framework for Model Context Protocol (MCP) servers</strong>
</p>

<p align="center">
  <a href="https://github.com/atakanatali/contextify-net/actions/workflows/ci.yml">
    <img src="https://github.com/atakanatali/contextify-net/actions/workflows/ci.yml/badge.svg" alt="CI" />
  </a>
  <a href="https://github.com/atakanatali/contextify-net/releases">
    <img src="https://img.shields.io/github/v/release/atakanatali/contextify-net?include_prereleases" alt="Release" />
  </a>
  <a href="https://www.nuget.org/packages/Contextify.AspNetCore">
    <img src="https://img.shields.io/nuget/v/Contextify.AspNetCore" alt="NuGet" />
  </a>
  <a href="https://www.nuget.org/packages/Contextify.Core">
    <img src="https://img.shields.io/nuget/dt/Contextify.Core" alt="Downloads" />
  </a>
  <a href="https://github.com/atakanatali/contextify-net/blob/main/LICENSE">
    <img src="https://img.shields.io/github/license/atakanatali/contextify-net" alt="License" />
  </a>
</p>

---

## Core Architecture

```mermaid
graph TB
    subgraph Client ["AI Client (Claude/GPT)"]
        CL[AI Assistant]
    end

    subgraph Contextify ["Contextify Stack"]
        direction TB
        T[Transport Layer<br/>HTTP / STDIO] --> DP[JSON-RPC Dispatcher]
        DP --> S[Security Layer<br/>Deny-by-Default]
        S --> C[Core Orchestrator]
        C --> CAT[Tool/Resource Catalog]
    end

    subgraph Providers ["Feature Providers"]
        OA[OpenAPI / Swagger] --> C
        NA[Native .NET Tools] --> C
        SY[System Actions] --> C
    end

    subgraph Config ["Configuration"]
        AS[AppSettings] -.-> DP
        CS[Consul] -.-> DP
    end

    CL <==> T
    
    style T fill:#4CAF50,color:#fff
    style S fill:#F44336,color:#fff
    style C fill:#2196F3,color:#fff
    style CAT fill:#9C27B0,color:#fff
```

### Package Structure

| Package | Description | NuGet |
|---------|-------------|-------|
| `Contextify.Abstractions` | Common interfaces and models | [![NuGet](https://img.shields.io/nuget/v/Contextify.Abstractions)](https://www.nuget.org/packages/Contextify.Abstractions) |
| `Contextify.Core` | Core framework and tool orchestration | [![NuGet](https://img.shields.io/nuget/v/Contextify.Core)](https://www.nuget.org/packages/Contextify.Core) |
| `Contextify.AspNetCore` | DI + ASP.NET Core integration | [![NuGet](https://img.shields.io/nuget/v/Contextify.AspNetCore)](https://www.nuget.org/packages/Contextify.AspNetCore) |
| `Contextify.Transport.Http` | HTTP transport for MCP | [![NuGet](https://img.shields.io/nuget/v/Contextify.Transport.Http)](https://www.nuget.org/packages/Contextify.Transport.Http) |
| `Contextify.Transport.Stdio` | STDIO transport for MCP | [![NuGet](https://img.shields.io/nuget/v/Contextify.Transport.Stdio)](https://www.nuget.org/packages/Contextify.Transport.Stdio) |
| `Contextify.OpenApi` | OpenAPI to MCP conversion | [![NuGet](https://img.shields.io/nuget/v/Contextify.OpenApi)](https://www.nuget.org/packages/Contextify.OpenApi) |

---

Contextify is a high-performance, modular library for building Model Context Protocol (MCP) servers in .NET. It allows you to expose your application's logic, data, and tools to AI assistants with enterprise-grade security and observability.

## Quickstart

### 1. Register Services
```csharp
// Add Contextify with Gateway and HTTP support
builder.Services.AddContextify()
    .AddHttpTransport(http => 
    {
        http.Endpoint = "/mcp/v1";
    })
    .AddAppSettingsPolicyProvider();
```

### 2. Map Endpoints
```csharp
app.MapContextifyMcp();
```

## Key Features

### 🛡️ Security First
- **Deny-by-Default**: No tool is exposed unless explicitly whitelisted in policy.
- **Request Validation**: Strict JSON-RPC schema and size limit enforcement.
- **Namespace Isolation**: Prevent tool naming collisions in multi-backend setups.

### 🔌 Modular Transports
- **HTTP**: Enterprise-ready transport with middleware support.
- **STDIO**: Native integration for CLI and local development workflows.

### 🔀 Gateway Aggregation
The **Contextify Gateway** acts as a unified hub for multiple MCP servers, providing:
- Concatenated tool catalogs with prefix namespacing.
- Resilient upstream health monitoring.
- Centralized policy enforcement across distributed services.

## Documentation

- [Architecture Guide](docs/architecture.md)
- [Configuration Reference](docs/architecture.md#configuration)
- [Gateway Setup](docs/architecture.md#gateway)
- [Official MCP Specification](https://modelcontextprotocol.io/)

## License

MIT License - see [LICENSE](LICENSE) for details.
