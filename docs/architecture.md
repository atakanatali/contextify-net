# Contextify Architecture

## Overview

Contextify is a modular .NET library for Model Context Protocol (MCP) server and tool management. This document describes the high-level architecture, module organization, dependency structure, and key design decisions.

## High-Level Modules

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Application Layer                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│  Contextify.AspNetCore   │  Contextify.OpenApi   │  Contextify.Gateway.Host│
│  (ASP.NET Core hosting)  │  (Swagger UI)         │  (Gateway executable)   │
└─────────────┬───────────────────────────────────────────┬───────────────────┘
              │                                           │
┌─────────────▼───────────────────────────────────────────▼───────────────────┐
│                           Integration Layer                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│  Contextify.Actions.Defaults   │  Contextify.Mcp.OfficialAdapter            │
│  (Rate limiting, caching)      │  (Official MCP SDK wrapper)                │
└─────────────┬───────────────────────────────────────────┬───────────────────┘
              │                                           │
┌─────────────▼───────────────────────────────────────────▼───────────────────┐
│                            Core Layer                                        │
├─────────────────────────────────────────────────────────────────────────────┤
│  Contextify.Core                 │  Contextify.Mcp.Abstractions             │
│  (Central orchestrator)          │  (MCP protocol abstractions)             │
│  Contextify.Actions.Abstractions │  Contextify.Logging                     │
│  (Action definitions)            │  (Logging abstractions)                  │
│  Contextify.Config.Abstractions  │                                           │
│  (Configuration providers)       │                                           │
└─────────────┬───────────────────────────────────────────┬───────────────────┘
              │                                           │
┌─────────────▼───────────────────────────────────────────▼───────────────────┐
│                          Transport Layer                                     │
├─────────────────────────────────────────────────────────────────────────────┤
│  Contextify.Transport.Http       │  Contextify.Transport.Stdio              │
│  (HTTP/HTTPS MCP transport)      │  (STDIO MCP transport)                   │
└─────────────────────────────────────────────────────────────────────────────┘
              │
┌─────────────▼───────────────────────────────────────────────────────────────┐
│                       Configuration Providers                               │
├─────────────────────────────────────────────────────────────────────────────┤
│  Contextify.Config.AppSettings   │  Contextify.Config.Consul                │
│  (appsettings.json provider)     │  (Consul distributed config)             │
└─────────────────────────────────────────────────────────────────────────────┘
              │
┌─────────────▼───────────────────────────────────────────────────────────────┐
│                          Tools & Utilities                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│  Contextify.LoadRunner          │                                           │
│  (Performance testing tool)     │                                           │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Module Descriptions

### Core Layer

The foundation of Contextify. Contains fundamental abstractions with no external dependencies beyond .NET BCL.

**Contextify.Core**
- Central orchestrator and DI container configuration
- Defines the context management contract
- References only abstractions (Actions, Logging, MCP, Config)
- Fluent builder API via `IContextifyBuilder` and `AddContextify()` extension
- Options entities: `ContextifyOptionsEntity`, `ContextifyLoggingOptionsEntity`,
  `ContextifyPolicyOptionsEntity`, `ContextifyActionsOptionsEntity`,
  `ContextifyToolExecutorOptionsEntity`
- Transport mode enum: `ContextifyTransportMode` (Auto, Http, Stdio, Both)
- `IContextifyJsonSchemaBuilderService` for reflection-based JSON Schema generation
  - Builds JSON Schema Draft 2020-12 from .NET types
  - Supports primitives, enums, nullable types, arrays, dictionaries, records/classes
  - Required fields determined by nullable annotations
  - Deterministic and cached by Type using ConcurrentDictionary
- `IContextifyToolExecutorService` for HTTP-based tool execution
  - Supports InProcessHttp execution mode using IHttpClientFactory
  - Builds URIs with route parameter expansion and query string generation
  - Handles JSON and plain text responses with proper parsing
  - Propagates authentication context via `ContextifyAuthContextDto`
  - Full async/await with cancellation token support
  - Error handling for timeouts, HTTP errors, and response parsing failures
- Execution mode enum: `ContextifyExecutionMode` (InProcessHttp, RemoteHttp)
- **Snapshot Management**:
  - `ContextifyCatalogBuilderService`: Builds catalog snapshots from policy.
  - `ContextifyCatalogProviderService`: Manages atomic swapping of catalog snapshots.
  - `IContextifyPolicyConfigProvider`: Abstraction for policy configuration with `DefaultContextifyPolicyConfigProvider` as fallback.

**Contextify.Mcp.Abstractions**
- Model Context Protocol abstractions
- `IMcpRuntime` interface (see below)
- Tool, resource, and prompt definitions
- Transport-agnostic MCP protocol types

**Contextify.Actions.Abstractions**
- `IContextifyAction` interface for action pipeline middleware
- `ContextifyInvocationContextDto` for invocation context
- `ContextifyToolResultDto` and `ContextifyToolErrorDto` for structured results
- Action pipeline abstractions with ordering and filtering support

**Contextify.Logging**
- Logging abstractions
- Structured logging support for MCP operations

**Contextify.Config.Abstractions**
- Configuration provider interfaces
- `IConfigProvider` and configuration snapshot abstractions

### Transport Layer

Handles communication between MCP clients and servers.

**Contextify.Transport.Http**
- HTTP/HTTPS transport implementation
- ASP.NET Core endpoint integration
- JSON-RPC over HTTP

**Contextify.Transport.Stdio**
- STDIO transport implementation
- For CLI tools and local development
- JSON-RPC over standard input/output

### Application Layer

Integration with .NET hosting and web frameworks.

**Contextify.AspNetCore**
- `AddContextify()` DI extension methods
- `AddContextifyEndpointDiscovery()` for endpoint discovery and parameter binding
- ASP.NET Core middleware and hosting
- `IContextifyEndpointDiscoveryService` for endpoint discovery
  - Discovers Minimal API and MVC controller endpoints
  - Extracts route patterns, HTTP methods, auth requirements, content types
  - Returns `ContextifyEndpointDescriptorEntity` for tool catalog integration
  - Deterministic ordering: HttpMethod → RouteTemplate → DisplayName
- `IContextifyEndpointParameterBinderService` for endpoint parameter binding analysis
  - Determines route/query/body parameters for endpoints
  - Builds unified input schema combining all parameter sources
  - Route parameters as properties, query parameters as properties, body as nested "body" property
  - Returns warnings when binding cannot be reliably inferred
  - Conservative fallback schema for complex scenarios

**Contextify.OpenApi**
- OpenAPI/Swagger integration for automatic tool descriptor enrichment
- `IContextifyOpenApiEnrichmentService` for enriching tools with OpenAPI metadata
- `IOpenApiOperationMatcher` for matching endpoints to OpenAPI operations
- `IOpenApiSchemaExtractor` for extracting input/output schemas from operations
- `ContextifyMappingGapReportDto` for diagnostic reporting of unmatched endpoints
- Supports multiple matching strategies: OperationId (highest), Route+Method (medium), DisplayName (low)
- Detects ApiExplorer/Swagger availability automatically
- Generates JSON Schema input/output schemas for tool descriptors

**Contextify.Actions.Defaults**
- Built-in action implementations for common cross-cutting concerns
- TimeoutAction (Order 100): Enforces timeout limits based on policy.TimeoutMs
- ConcurrencyAction (Order 110): Limits concurrent executions based on policy.ConcurrencyLimit
- RateLimitAction (Order 120): Enforces rate limits based on policy.RateLimitPolicy
- Supports tenant-aware rate limiting when tenant context is available
- Uses System.Threading.RateLimiter for efficient permit management
- Builder extension: `AddDefaults()` registers all actions in DI

### Configuration Layer

Multiple configuration provider implementations.

**Contextify.Config.AppSettings**
- Loads configuration from `appsettings.json`
- Implements `ContextifyAppSettingsPolicyConfigProvider`
- Environment-specific overrides
- Reload on file change support

**Contextify.Config.Consul**
- Consul KV store integration
- Distributed configuration
- Watch/reload on remote changes

### Adapter Layer

**Contextify.Mcp.OfficialAdapter**
- Adapter for the official ModelContextProtocol SDK (v0.6.0-preview.1)
- Alternative to native implementation
- Uses marker interface `IOfficialMcpRuntimeMarker` for runtime auto-selection
- Extension method `AddContextifyOfficialMcpAdapter()` registers the official runtime
- When registered, `McpRuntimeResolver` automatically selects it over native fallback
- No hard reference from Core to OfficialAdapter - uses reflection-based detection

### Gateway Layer

**Contextify.Gateway.Core**
- Gateway routing and aggregation logic
- Multi-backend tool catalog aggregation with atomic snapshot swapping
- Request/response transformation and dispatching
- `IContextifyGatewayUpstreamRegistry` - Interface for upstream discovery and retrieval
- `StaticGatewayUpstreamRegistry` - Static configuration-based registry that filters enabled upstreams
- `ContextifyGatewayCatalogAggregatorService` - Aggregates tool catalogs from multiple upstreams with immutable snapshots
- `ContextifyGatewayToolDispatcherService` - Dispatches tool invocation requests to appropriate upstreams
- `ContextifyGatewayToolNameService` - Handles tool name transformation with namespace prefixes
- `ContextifyGatewayUpstreamHealthService` - Async health probing with manifest/MCP fallback strategy
- `ContextifyGatewayToolPolicyService` - Gateway-level tool access policy with wildcard pattern matching
  - `IsAllowed(externalToolName)` - Determines if a tool is allowed based on policy
  - `FilterAllowedTools(toolNames)` - Filters tool names to only allowed tools
  - Supports '*' wildcard patterns for prefix, suffix, and contains matching
  - Precompiled patterns for zero-allocation matching in high-concurrency scenarios
  - Security-first: denied patterns always override allowed patterns
  - Configured via `ContextifyGatewayOptionsEntity.AllowedToolPatterns` and `DeniedToolPatterns`
- `ContextifyGatewayAuditService` - Structured logging and correlation ID propagation for high-concurrency auditing.
- `IContextifyGatewayResiliencyPolicy` - Abstraction for upstream call resiliency (e.g., `ContextifyGatewayNoRetryPolicy`).
- `ContextifyGatewayUpstreamHealthProbeResultEntity` - Health probe result with status, latency, and error details
- `ContextifyGatewayUpstreamHealthProbeStrategy` enum - Manifest vs McpToolsList probe strategies
- Snapshot entities for thread-safe catalog access (ContextifyGatewayCatalogSnapshotEntity, ContextifyGatewayToolRouteEntity, ContextifyGatewayUpstreamStatusEntity)
- **HttpClient Integration**: Uses `IHttpClientFactory` via the named "ContextifyGateway" client for efficient connection pooling.

**Contextify.Gateway.Host**
- Minimal ASP.NET Core host exposing aggregated MCP endpoint
- `Program.cs` - Main entry point with service registration and endpoint mapping
- `ContextifyGatewayCatalogRefreshHostedService` - Background service for periodic catalog refresh
- Exposes POST /mcp for JSON-RPC 2.0 MCP requests (tools/list, tools/call)
- Exposes GET /.well-known/contextify/manifest for gateway metadata
- Exposes GET /contextify/gateway/diagnostics for operational monitoring
- Uses SemaphoreSlim to prevent overlapping catalog refresh operations
- Binds configuration from "Contextify:Gateway" section

## Gateway Core Responsibilities

The Gateway layer provides unified access to multiple upstream MCP servers. Its core responsibilities are:

### 1. Upstream Discovery and Registry

- **Interface**: `IContextifyGatewayUpstreamRegistry`
- **Implementation**: `StaticGatewayUpstreamRegistry`
- **Responsibility**: Discover enabled upstreams and provide them to the aggregator
- **Filtering**: Excludes upstreams where `Enabled = false`
- **Extension Point**: Implement `IContextifyGatewayUpstreamRegistry` for dynamic discovery (Consul, K8s service discovery)

### 2. Catalog Aggregation

The Gateway aggregates tool catalogs from all healthy upstreams into a unified snapshot:

**Flow:**
```
1. Discovery: Get enabled upstreams from registry
2. Parallel Fetch: Fetch tools from each upstream concurrently
3. Namespace Application: Apply namespace prefixes (e.g., "weather.get_forecast")
4. Policy Filtering: Apply gateway-level allow/deny patterns
5. Snapshot Creation: Build immutable catalog snapshot
6. Atomic Swap: Use Interlocked.Exchange for thread-safe replacement
```

**Key Components:**
- `ContextifyGatewayCatalogAggregatorService` - Orchestrates aggregation
- `ContextifyGatewayToolNameService` - Applies namespace transformation
- `ContextifyGatewayToolPolicyService` - Filters tools by policy
- `ContextifyGatewayCatalogSnapshotEntity` - Immutable snapshot for thread-safe reads

### 3. Tool Dispatch

When a tool invocation request arrives, the Gateway routes it to the appropriate upstream:

**Flow:**
```
1. Policy Check: Validate tool access at gateway level
2. Route Resolution: Look up tool route from catalog snapshot
3. Health Check: Verify upstream is healthy (return error if not)
4. Request Forwarding: Forward MCP call to upstream HTTP endpoint
5. Response Handling: Parse and return upstream response
6. Audit Logging: Log start/end events with correlation tracking
```

**Key Components:**
- `ContextifyGatewayToolDispatcherService` - Routes requests to upstreams
- `ContextifyGatewayUpstreamHealthService` - Tracks upstream health status
- `ContextifyGatewayAuditService` - Structured logging for observability

### 4. Upstream Health Monitoring

The Gateway continuously monitors upstream health and excludes unhealthy servers from the catalog:

**Health Probe Strategy:**
1. **Manifest Probe** (Primary): GET `/.well-known/contextify/manifest` - fastest health check
2. **MCP Tools List** (Fallback): POST `/mcp` with `tools/list` request - validates MCP protocol

**Failure Modes:**
- Connection timeout (configured per upstream)
- HTTP 5xx errors
- MCP protocol errors (invalid JSON-RPC response)
- Network failures

**Partial Availability Strategy:**
- Unhealthy upstreams are excluded from catalog snapshot
- Healthy upstreams continue serving tools
- Background probes retry failed upstreams on next refresh
- Automatic recovery when upstream returns to healthy state
- No manual intervention required for failover/failback

### 5. Gateway-Level Security

The Gateway enforces tool access policies before forwarding requests to upstreams:

**Policy Configuration:**
```json
{
  "AllowedToolPatterns": ["weather:*", "database:read:*"],
  "DeniedToolPatterns": ["database:delete:*", "admin:*"]
}
```

**Policy Evaluation Order:**
1. If `DenyByDefault = true`: Only tools matching `AllowedToolPatterns` are accessible
2. If `DenyByDefault = false`: All tools accessible except those matching `DeniedToolPatterns`
3. Denied patterns always override allowed patterns (security-first)

**Wildcard Support:**
- `weather:*` - All tools with "weather." prefix
- `*:read` - All tools ending with ".read"
- `*query*` - All tools containing "query"

### 6. Resiliency and Retry Policies

The Gateway supports configurable retry policies for transient failures:

**Built-in Policies:**
- `ContextifyGatewayNoRetryPolicy` - Fail-fast (default)
- `ContextifyGatewaySimpleRetryPolicy` - Retry on transient errors with backoff

**Custom Policies:**
Implement `IContextifyGatewayResiliencyPolicy` for custom retry logic:

```csharp
public interface IContextifyGatewayResiliencyPolicy
{
    /// Executes the operation with retry logic.
    Task<T> ExecuteAsync<T>(
        string upstreamName,
        Func<Task<T>> operation,
        CancellationToken cancellationToken);
}
```

## Gateway Flow Diagrams

### Catalog Aggregation Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     Catalog Refresh Trigger                             │
│                    (Timer or Startup)                                   │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│              ContextifyGatewayCatalogRefreshHostedService               │
│              Acquires semaphore (prevents overlapping refreshes)        │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                   IContextifyGatewayUpstreamRegistry                    │
│                   Returns enabled upstreams list                        │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│              ContextifyGatewayCatalogAggregatorService                  │
│              Parallel fetch from all upstreams                          │
└────────────┬────────────────────────────────────────┬───────────────────┘
             │                                        │
    ┌────────▼────────┐                    ┌─────────▼─────────┐
    │ Upstream A      │                    │ Upstream B        │
    │ (HTTP Fetch)    │                    │ (HTTP Fetch)      │
    │ tools/list      │                    │ tools/list        │
    └────────┬────────┘                    └─────────┬─────────┘
             │                                        │
             │  Tools: [get_forecast, get_alerts]     │  Tools: [query, exec]
             └────────────────┬───────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                   ContextifyGatewayToolNameService                      │
│                   Apply namespace prefixes                              │
│                   weather.get_forecast, db.query                        │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                   ContextifyGatewayToolPolicyService                    │
│                   Filter by gateway allow/deny patterns                 │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                   ContextifyGatewayCatalogSnapshotEntity                │
│                   Build immutable snapshot                              │
│                   Interlocked.Exchange(swap)                            │
└─────────────────────────────────────────────────────────────────────────┘
```

### Tool Dispatch Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                   POST /mcp (JSON-RPC 2.0 Request)                     │
│                   tools/call with tool name and arguments              │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                   ContextifyGatewayToolDispatcherService                │
│                   Parse tool name and extract namespace                 │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                   ContextifyGatewayToolPolicyService                    │
│                   Check if tool is allowed by gateway policy            │
└────────────┬───────────────────────────────┬───────────────────────────┘
             │                               │
      Allowed                          Blocked/Not Found
             │                               │
             ▼                               ▼
┌─────────────────────────────┐    ┌─────────────────────────────────────┐
│  Resolve tool route from    │    │  Return error response              │
│  catalog snapshot           │    │  (tool not allowed or not found)   │
└────────────┬────────────────┘    └─────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                   ContextifyGatewayUpstreamHealthService                │
│                   Check if upstream is healthy                          │
└────────────┬───────────────────────────────┬───────────────────────────┘
             │                               │
      Healthy                          Unhealthy
             │                               │
             ▼                               ▼
┌─────────────────────────────┐    ┌─────────────────────────────────────┐
│  Forward request to         │    │  Return error response              │
│  upstream HTTP endpoint     │    │  (upstream unavailable)             │
└────────────┬────────────────┘    └─────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                   IContextifyGatewayResiliencyPolicy                    │
│                   Execute with retry policy                             │
└────────────┬───────────────────────────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                   Upstream MCP Server                                   │
│                   Process tool call and return result                   │
└────────────┬───────────────────────────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                   ContextifyGatewayAuditService                         │
│                   Log invocation result with correlation ID             │
└────────────┬───────────────────────────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                   Return JSON-RPC 2.0 Response to Client               │
└─────────────────────────────────────────────────────────────────────────┘
```

## Dependency Diagram (Text-Based)

```
Contextify.Gateway.Host
    └── Contextify.Gateway.Core
            ├── Contextify.Mcp.Abstractions
            └── Contextify.Transport.Http
                    └── Contextify.Mcp.Abstractions

Contextify.AspNetCore
    ├── Contextify.Core
    │       ├── Contextify.Mcp.Abstractions
    │       ├── Contextify.Actions.Abstractions
    │       ├── Contextify.Logging
    │       └── Contextify.Config.Abstractions
    ├── Contextify.Config.AppSettings (optional)
    └── Contextify.Config.Consul (optional)
            └── Contextify.Config.Abstractions

Contextify.OpenApi
    ├── Contextify.AspNetCore
    └── Contextify.Mcp.Abstractions

Contextify.Actions.Defaults
    ├── Contextify.Actions.Abstractions
    └── Contextify.Logging

Contextify.Mcp.OfficialAdapter
    ├── Contextify.Mcp.Abstractions
    └── Contextify.Core (for IOfficialMcpRuntimeMarker detection via reflection)

Contextify.Transport.Http
    └── Contextify.Mcp.Abstractions

Contextify.Transport.Stdio
    └── Contextify.Mcp.Abstractions
```

## IMcpRuntime Abstraction

The `IMcpRuntime` interface is the central abstraction for MCP protocol operations. It provides:

```csharp
/// Abstraction for MCP runtime operations.
/// Supports both native implementation and official SDK adapter.
public interface IMcpRuntime
{
    /// Starts the MCP runtime with the specified transport.
    Task StartAsync(IMcpTransport transport, CancellationToken cancellationToken = default);

    /// Stops the MCP runtime gracefully.
    Task StopAsync(CancellationToken cancellationToken = default);

    /// Registers a tool that can be invoked by MCP clients.
    void RegisterTool(IMcpTool tool);

    /// Registers a resource that can be read/written by MCP clients.
    void RegisterResource(IMcpResource resource);

    /// Lists all registered tools, optionally filtered by namespace.
    Task<IReadOnlyList<IMcpTool>> ListToolsAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default);

    /// Lists all registered resources, optionally filtered by namespace.
    Task<IReadOnlyList<IMcpResource>> ListResourcesAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default);

    /// Invokes a tool by name with the specified arguments.
    Task<IMcpToolResult> InvokeToolAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken = default);

    /// Reads a resource by URI.
    Task<IMcpResourceContent> ReadResourceAsync(string resourceUri, CancellationToken cancellationToken = default);
}
```

### Runtime Modes

#### Native Mode (Default)

Contextify's native implementation of the MCP protocol.

```json
{
  "Contextify": {
    "Runtime": {
      "Mode": "Native"
    }
  }
}
```

**Benefits:**
- Full control over implementation
- Optimized for .NET runtime
- No external dependencies

#### OfficialAdapter Mode

Wraps the official `ModelContextProtocol` NuGet package (v0.6.0-preview.1).

```csharp
// Register official adapter before AddContextify
services.AddContextifyOfficialMcpAdapter()
    .AddContextify();
```

**Benefits:**
- Official protocol support
- Community-tested implementation
- Easier protocol updates
- Zero-dependency when not referenced

### Runtime Selection

The runtime is selected automatically at startup via `McpRuntimeResolver`:

```csharp
internal static class McpRuntimeResolver
{
    /// Registers the appropriate runtime based on available services.
    internal static void RegisterMcpRuntime(IServiceCollection services, ILogger logger);

    /// Detects if official MCP SDK adapter is registered via reflection.
    private static bool HasOfficialMcpRuntimeMarker(IServiceCollection services);
}
```

**Auto-Selection Logic:**
1. If `IOfficialMcpRuntimeMarker` is detected in the service collection, the official adapter is used
2. Otherwise, the native fallback (`ContextifyNativeMcpRuntime`) is registered
3. Detection uses reflection-based type loading to avoid hard dependencies
4. Core does not directly reference OfficialAdapter assembly

**Usage:**

```csharp
// Use native runtime (default)
services.AddContextify();

// Use official SDK adapter
services.AddContextifyOfficialMcpAdapter()
    .AddContextify();
```

## Thread-Safety and Snapshot Catalog Strategy

### Thread Safety

All public APIs in Contextify are thread-safe. Internal state is protected using:

1. **Concurrent Collections** - `ConcurrentDictionary` for tool and resource registries
2. **Immutable Snapshots** - Configuration snapshots are never modified after creation
3. **Async/Await** - All I/O operations are async to avoid thread blocking

### Configuration Snapshots

Configuration is loaded into immutable snapshots:

```csharp
/// Immutable snapshot of Contextify configuration.
/// Created once and never modified.
public sealed class ContextifySnapshot
{
    public required SecuritySnapshot Security { get; init; }
    public required TransportSnapshot Transport { get; init; }
    public required RuntimeSnapshot Runtime { get; init; }
}
```

Benefits:
- No locks needed during configuration reads
- Safe to cache and pass between threads
- Enables configuration hot-reload without affecting in-flight requests

## No Circular Dependency Policy

Contextify enforces strict layering to prevent circular dependencies:

```
Core → Abstractions → Transport → Application → Gateway
```

**Rules:**
1. Core may reference Abstractions
2. Abstractions may NOT reference Core
3. Application may reference Core and Abstractions
4. Core may NOT reference Application
5. Transport may only reference Abstractions

**Enforcement:**
- Architectural tests verify dependency rules
- Project references are validated at build time
- Any circular dependency is a build error

## Design Patterns

Contextify uses established patterns to maintain code quality:

### Factory Pattern
- `IMcpRuntimeFactory` - Creates the appropriate runtime implementation
- `IConfigProviderFactory` - Creates configuration providers

### Adapter Pattern
- `Contextify.Mcp.OfficialAdapter` - Adapts the official MCP SDK to Contextify abstractions

### Strategy Pattern
- `IMcpTransport` - Different transport strategies (HTTP, STDIO)
- `IConfigProvider` - Different configuration strategies

### Command/Handler Pattern
- `IContextifyAction` - Tool execution as middleware pipeline
- `ICommand<T>` and `ICommandHandler<T>` - Generic command handling

### Rule Engine
For complex validation logic (3+ if statements):

```csharp
/// Defines a validation rule.
public interface IRule<in T>
{
    /// Evaluates the rule against the input.
    RuleResult Evaluate(T input);
}

/// Executes multiple rules and aggregates results.
public interface IRuleExecutor<T>
{
    /// Executes all registered rules.
    Task<IReadOnlyList<RuleResult>> ExecuteAsync(T input, CancellationToken cancellationToken = default);
}
```

## Naming Conventions

All Contextify code follows enterprise-grade naming conventions:

| Type | Postfix | Example |
|------|---------|---------|
| Entities (database entities) | `*Entity` | `UserEntity`, `OrderEntity` |
| DTOs (data transfer objects) | `*Dto` | `CreateToolDto`, `ToolResultDto` |
| Services (business logic) | `*Service` | `ToolRegistrationService` |
| Repositories (data access) | `*Repository` | `ToolRepository` |
| Middleware (ASP.NET Core) | `*Middleware` | `McpMiddleware` |
| Configuration objects | `*Options` | `ContextifyOptions`, `SecurityOptions` |
| Commands (CQRS) | `*Command` | `InvokeToolCommand` |
| Command handlers | `*Handler` | `InvokeToolHandler` |
| Projections (queries) | `*Projection` | `ToolListProjection` |

## Configuration Sources

Contextify supports multiple configuration sources, loaded in priority order:

1. **Code defaults** - Lowest priority
2. **appsettings.json** - Overridden by higher priority
3. **Environment variables** - Overridden by higher priority
4. **Consul** - Highest priority (when enabled)

Example with multiple sources:

```csharp
builder.Services.AddContextify(options =>
{
    // Load from appsettings
    options.LoadFromAppSettings();

    // Load from Consul (overrides appsettings)
    options.LoadFromConsul();

    // Load from environment (overrides everything)
    options.LoadFromEnvironment();
});

## Release Automation

Contextify implements automated release notes generation and package publishing using GitHub Actions. This ensures consistent, accurate releases based on manual triggers and versioning.

### Release Workflow

The GitHub Actions workflows (`.github/workflows/ci.yml` and `.github/workflows/publish.yml`) automate the entire process:

1. **CI Trigger**: Every push to `main` or pull request triggers the CI build and test suite.
2. **Manual Publish**: The `Publish to NuGet` workflow is manually triggered with a version input (e.g., `1.0.0`) and optional prerelease flag.
3. **Build & Test**: Builds the solution in Release configuration and runs all unit/integration tests on the latest .NET SDK.
4. **Versioning**: Automatically applies the specified version to all project files during the build and pack steps.
5. **Multi-Package NuGet Publishing**: Creates and pushes `.nupkg` and `.snupkg` files for all 10+ Contextify modules to NuGet.org.
6. **Git Tagging**: Automatically creates and pushes a version tag (e.g., `v1.0.0`) to the repository.
7. **GitHub Release**: Creates a GitHub release with automated notes, installation instructions, and attached package artifacts.

### Supply Chain Security

Contextify implements supply-chain best practices for secure package distribution:

1. **Deterministic Builds**: `ContinuousIntegrationBuild` flag is enabled in CI to ensure reproducible builds with proper SourceLink integration.
2. **Locked Dependencies**: CI uses `dotnet restore --locked-mode` to ensure package versions match the committed lock files.
3. **Checksum Verification**: Every release includes SHA256 checksums for all published packages.
4. **Symbol Packages**: Symbols are published as `.snupkg` files to facilitate source-linked debugging.

## CI/CD Quality Gates

Contextify enforces code quality through automated CI checks before any code is merged.

### Dependency Validation

The CI pipeline includes a dedicated `dependency-validation` job that runs before build:

1. **Circular Reference Detection**
   - Scans all `.csproj` files for `ProjectReference` elements
   - Builds a dependency graph and performs DFS cycle detection
   - Fails the build if any circular dependency is found
   - Script: `.github/scripts/detect-circular-references.sh`

2. **Central Package Management Verification**
   - Ensures all package versions are declared in `Directory.Packages.props`
   - Scans for `PackageVersion` elements in individual `.csproj` files
   - Fails the build if versions are duplicated outside central management

3. **Central Versioning Verification**
   - Ensures no `<Version>` attributes exist in `.csproj` files
   - All versions must come from `Directory.Build.props`
   - Prevents version drift across projects

4. **PackageReference Version Validation**
   - Ensures no `Version=` attributes in `PackageReference` elements
   - All package versions must come from `Directory.Packages.props`
   - Enforces centralized package version management

5. **Transitive Package Audit**
   - Runs `dotnet list package --include-transitive` for dependency auditing
   - Helps identify unexpected version conflicts

### Build-Test Pipeline

After dependency validation passes:
1. **Locked Mode Restore** - `dotnet restore --locked-mode` ensures deterministic package restoration
2. **Code Format Verification** - `dotnet format --verify-no-changes` enforces consistent formatting
3. **Build** - Solution in Release configuration with warnings as errors
4. **Unit Tests** - Full test suite with code coverage
5. **Integration Tests** - End-to-end scenario validation
6. **Pack** - NuGet package creation with README verification
7. **Checksum Generation** - SHA256 checksums for all packages

### Supply Chain Security

Contextify implements supply-chain best practices for secure package distribution:

1. **Deterministic Builds**
   - `ContinuousIntegrationBuild` environment variable set in all CI workflows
   - Automatic detection for GitHub Actions, Azure DevOps, Jenkins, GitLab CI
   - Enables proper SourceLink integration for debugging

2. **Locked Dependencies**
   - CI uses `--locked-mode` flag for `dotnet restore`
   - Ensures package versions match `packages.lock.json` (when committed)
   - Fails build if dependencies would change unexpectedly

3. **Code Formatting Guardrails**
   - `.editorconfig` defines `dotnet_format_*` settings
   - CI validates formatting with `dotnet format --verify-no-changes`
   - Prevents inconsistent style from being merged

4. **Release Artifacts**
   - **NuGet packages** (.nupkg) - Published to NuGet.org
   - **Symbol packages** (.snupkg) - Published for source-linked debugging
   - **SHA256 checksums** (checksums.txt) - For package integrity verification
   - **CHANGELOG references** - Each release links to CHANGELOG.md

### Release Workflow Improvements

The release workflow (`.github/workflows/release.yml`) includes:

1. **Locked Mode Restore** - Consistent with CI workflow
2. **Separate Symbol Package Publishing** - Explicit `.snupkg` push to NuGet.org
3. **SHA256 Checksum Generation** - Automatic checksum file creation
4. **CHANGELOG Extraction** - Reads version-specific entries from CHANGELOG.md
5. **Enhanced Release Notes** - Includes installation instructions, verification steps, and changelog references

## Pull Request Template

All pull requests must use the provided template and complete the checklist:

- Code Quality Rules (architectural thinking, no Turkish characters, no God classes, etc.)
- Validation (tests executed, docs updated if needed, no Turkish characters)
- Type of Change selection (bug fix, feature, breaking change, etc.)

## Issue Templates

Standard templates ensure consistent issue reporting:

- **Bug Report**: Steps to reproduce, expected/actual behavior, environment details
- **Feature Request**: Problem statement, proposed solution, API design, impact assessment

## Transport Behaviors

### HTTP Transport (Contextify.Transport.Http)

- **Protocol**: JSON-RPC 2.0 over HTTP/HTTPS
- **Content-Type**: `application/json`
- **Endpoint**: Configurable base path (default: `/mcp`)
- **Methods**: POST for tool invocation, GET for resource listing
- **Streaming**: Not supported (request/response pattern only)
- **Authentication**: Bearer token propagation via `ContextifyAuthContextDto`
- **Timeout**: Configurable via `ContextifyToolExecutorOptionsEntity.TimeoutMs`

### STDIO Transport (Contextify.Transport.Stdio)

- **Protocol**: JSON-RPC 2.0 over standard input/output
- **Use Case**: CLI tools, local development, IDE integrations
- **Lifecycle**: Process-bound, terminates on parent process exit
- **Concurrency**: Single-threaded message processing
- **Buffering**: Line-buffered JSON messages
- **Signal Handling**: Graceful shutdown on SIGTERM/SIGINT

### Transport Selection

The `ContextifyTransportMode` enum controls which transports are enabled:

| Mode | HTTP | STDIO | Description |
|------|------|-------|-------------|
| `Auto` | ✓ | ✓ | Enables both; selects based on environment |
| `Http` | ✓ | | HTTP only for web scenarios |
| `Stdio` | | ✓ | STDIO only for CLI scenarios |
| `Both` | ✓ | ✓ | Explicitly enables both transports |

## Final Module Responsibilities

### Core Layer (Foundation)

| Module | Responsibility | Dependencies |
|--------|---------------|--------------|
| `Contextify.Core` | Central orchestrator, DI configuration, JSON schema generation | Abstractions only |
| `Contextify.Mcp.Abstractions` | MCP protocol abstractions, tool/resource/prompt definitions | None |
| `Contextify.Actions.Abstractions` | Action pipeline middleware interfaces | None |
| `Contextify.Logging` | Structured logging abstractions | None |
| `Contextify.Config.Abstractions` | Configuration provider interfaces, snapshot abstractions | None |

### Transport Layer (Communication)

| Module | Responsibility | Dependencies |
|--------|---------------|--------------|
| `Contextify.Transport.Http` | JSON-RPC over HTTP/HTTPS, ASP.NET Core integration | Mcp.Abstractions |
| `Contextify.Transport.Stdio` | JSON-RPC over STDIO, CLI tool support | Mcp.Abstractions |

### Application Layer (Hosting)

| Module | Responsibility | Dependencies |
|--------|---------------|--------------|
| `Contextify.AspNetCore` | ASP.NET Core hosting, endpoint discovery, parameter binding | Core, Actions.Abstractions |
| `Contextify.OpenApi` | OpenAPI/Swagger integration, schema enrichment | AspNetCore, Mcp.Abstractions |
| `Contextify.Actions.Defaults` | Built-in actions (timeout, rate limiting, concurrency) | Actions.Abstractions, Logging |

### Configuration Layer (Data)

| Module | Responsibility | Dependencies |
|--------|---------------|--------------|
| `Contextify.Config.AppSettings` | appsettings.json provider, environment-specific overrides | Config.Abstractions |
| `Contextify.Config.Consul` | Consul KV store integration, distributed configuration watch | Config.Abstractions |

### Adapter Layer (Integration)

| Module | Responsibility | Dependencies |
|--------|---------------|--------------|
| `Contextify.Mcp.OfficialAdapter` | Official MCP SDK adapter (v0.6.0-preview.1) | Mcp.Abstractions, Core (reflection) |

### Gateway Layer (Aggregation)

| Module | Responsibility | Dependencies |
|--------|---------------|--------------|
| `Contextify.Gateway.Core` | Multi-backend routing, catalog aggregation, tool name transformation, upstream registry and health probing, gateway-level tool access policy with wildcard pattern matching | Mcp.Abstractions, Microsoft.Extensions.Options, Microsoft.Extensions.Logging |
| `Contextify.Gateway.Host` | Standalone gateway executable with MCP endpoint, catalog refresh hosted service, manifest and diagnostics endpoints | Gateway.Core, AspNetCore, Config.AppSettings |

### Tools & Utilities Layer

| Module | Responsibility | Dependencies |
|--------|---------------|--------------|
| `Contextify.LoadRunner` | Lightweight load testing tool for MCP endpoints, measures latency (P50/P95/P99), error rate, and throughput, outputs JSON reports to artifacts/ | Microsoft.Extensions.Hosting, Microsoft.Extensions.Logging, Microsoft.Extensions.Http, System.Text.Json |

## Dependency Direction Rules

Strict layering is enforced to maintain architectural integrity:

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                        │
│  (May reference Core + Abstractions, not referenced by Core) │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                       Core Layer                            │
│  (May reference Abstractions, not referenced by Abstractions)│
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                   Abstractions Layer                        │
│  (No dependencies on other Contextify modules)              │
└─────────────────────────────────────────────────────────────┘
```

**Rules:**
1. Core may reference Abstractions
2. Abstractions may NOT reference Core
3. Application may reference Core and Abstractions
4. Core may NOT reference Application
5. Transport may only reference Abstractions
6. Gateway may reference Core and Transport
7. Configuration may only reference Config.Abstractions
8. Tools (LoadRunner) are standalone and may not reference other Contextify modules (dependency-free for flexible deployment)

**Violation Detection:**
- CI automatically detects circular dependencies via `.github/scripts/detect-circular-references.sh`
- Architectural unit tests verify dependency rules
- Build fails if any circular reference is detected

