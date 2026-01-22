# Contextify Samples

This directory contains sample applications demonstrating the features of Contextify.NET.

## 1. Basic.Stdio
A minimal example of an MCP server running over STDIO (Standard Input/Output). This is the standard transport for local MCP agents.

**Run:**
```bash
dotnet run --project samples/Basic.Stdio/Basic.Stdio.csproj
```

## 2. Basic.Http
A minimal example of an MCP server running over HTTP (SSE). Useful for remote debugging or remote agents.

**Run:**
```bash
dotnet run --project samples/Basic.Http/Basic.Http.csproj
```

## 3. Advanced.Features
Demonstrates advanced capabilities including:
- **OpenAPI Integration:** Auto-generated Swagger UI for your tools.
- **Policy Enforcement:** Whitelisting/Blacklisting tools.
- **Detailed Logging:** Inspecting JSON-RPC payloads.

**Run:**
```bash
dotnet run --project samples/Advanced.Features/Advanced.Features.csproj
```
Access Swagger UI at: `http://localhost:5000/swagger`

## 4. Configuration.Demo
Shows how to configure Contextify using `appsettings.json`.
- Change `Contextify:Policy:DenyByDefault` to `true` in `appsettings.json` and watch the policy update (if running with reload enabled).

**Run:**
```bash
dotnet run --project samples/Configuration.Demo/Configuration.Demo.csproj
```
