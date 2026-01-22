using System.Net;
using System.Text;
using System.Text.Json;
using Contextify.Gateway.Core.Configuration;
using Contextify.Gateway.Core.RateLimit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Gateway.RateLimit;

/// <summary>
/// Unit tests for ContextifyGatewayRateLimitMiddleware.
/// Verifies rate limiting behavior for MCP calls with different scopes and tenant configurations.
/// </summary>
public sealed class ContextifyGatewayRateLimitMiddlewareTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that constructor throws when next delegate is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenNextIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var logger = Mock.Of<ILogger<ContextifyGatewayRateLimitMiddleware>>();
        var options = Options.Create(new ContextifyGatewayRateLimitOptionsEntity());
        var tenantOptions = Options.Create(new ContextifyGatewayTenantResolutionOptionsEntity());

        // Act
        var act = () => new ContextifyGatewayRateLimitMiddleware(
            null!,
            logger,
            options,
            tenantOptions);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("next");
    }

    /// <summary>
    /// Tests that constructor throws when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;
        var options = Options.Create(new ContextifyGatewayRateLimitOptionsEntity());
        var tenantOptions = Options.Create(new ContextifyGatewayTenantResolutionOptionsEntity());

        // Act
        var act = () => new ContextifyGatewayRateLimitMiddleware(
            next,
            null!,
            options,
            tenantOptions);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region Disabled Rate Limiting Tests

    /// <summary>
    /// Tests that when rate limiting is disabled, requests pass through.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenRateLimitingDisabled_CallsNextDelegate()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var options = Options.Create(new ContextifyGatewayRateLimitOptionsEntity { Enabled = false });
        var tenantOptions = Options.Create(new ContextifyGatewayTenantResolutionOptionsEntity());
        var logger = Mock.Of<ILogger<ContextifyGatewayRateLimitMiddleware>>();

        var middleware = new ContextifyGatewayRateLimitMiddleware(next, logger, options, tenantOptions);
        var context = CreateHttpContext("/mcp", CreateToolsCallJsonRpc("test.tool"));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    #endregion

    #region Non-MCP Endpoint Tests

    /// <summary>
    /// Tests that non-MCP endpoints bypass rate limiting.
    /// </summary>
    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/values")]
    [InlineData("/")]
    public async Task InvokeAsync_WhenNotMcpEndpoint_BypassesRateLimiting(string path)
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var options = Options.Create(new ContextifyGatewayRateLimitOptionsEntity
        {
            Enabled = true,
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Global,
                1,
                60000)
        });
        var tenantOptions = Options.Create(new ContextifyGatewayTenantResolutionOptionsEntity());
        var logger = Mock.Of<ILogger<ContextifyGatewayRateLimitMiddleware>>();

        var middleware = new ContextifyGatewayRateLimitMiddleware(next, logger, options, tenantOptions);
        var context = CreateHttpContext(path);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    #endregion

    #region Independent Tenant Quotas Tests

    /// <summary>
    /// Tests that different tenants have independent quota allocations.
    /// Tenant A can make requests up to its limit without affecting Tenant B's quota.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_DifferentTenantsHaveIndependentQuotas_EachTenantHasOwnLimit()
    {
        // Arrange
        var nextCalledCount = 0;
        RequestDelegate next = _ =>
        {
            nextCalledCount++;
            return Task.CompletedTask;
        };

        var options = Options.Create(new ContextifyGatewayRateLimitOptionsEntity
        {
            Enabled = true,
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Tenant,
                permitLimit: 2,
                windowMs: 60000,
                queueLimit: 0)
        });
        var tenantOptions = Options.Create(new ContextifyGatewayTenantResolutionOptionsEntity());
        var logger = Mock.Of<ILogger<ContextifyGatewayRateLimitMiddleware>>();

        var middleware = new ContextifyGatewayRateLimitMiddleware(next, logger, options, tenantOptions);

        // Act - Tenant A makes 2 requests (should succeed)
        var contextA1 = CreateHttpContextWithTenant("/mcp", "tenant-a", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(contextA1);

        var contextA2 = CreateHttpContextWithTenant("/mcp", "tenant-a", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(contextA2);

        // Tenant A makes 3rd request (should be rate limited)
        var contextA3 = CreateHttpContextWithTenant("/mcp", "tenant-a", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(contextA3);

        // Tenant B makes 2 requests (should succeed - independent quota)
        var contextB1 = CreateHttpContextWithTenant("/mcp", "tenant-b", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(contextB1);

        var contextB2 = CreateHttpContextWithTenant("/mcp", "tenant-b", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(contextB2);

        // Tenant B makes 3rd request (should be rate limited)
        var contextB3 = CreateHttpContextWithTenant("/mcp", "tenant-b", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(contextB3);

        // Assert
        nextCalledCount.Should().Be(4); // 2 for tenant A, 2 for tenant B
        contextA1.Response.StatusCode.Should().Be(200);
        contextA2.Response.StatusCode.Should().Be(200);
        contextA3.Response.StatusCode.Should().Be(429); // Rate limited
        contextB1.Response.StatusCode.Should().Be(200);
        contextB2.Response.StatusCode.Should().Be(200);
        contextB3.Response.StatusCode.Should().Be(429); // Rate limited
    }

    #endregion

    #region TenantTool Scope Tests

    /// <summary>
    /// Tests that TenantTool scope limits per tool per tenant independently.
    /// Tenant A using tool1 should have a separate quota from Tenant A using tool2.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_TenantToolScope_LimitsPerToolPerTenant()
    {
        // Arrange
        var nextCalledCount = 0;
        RequestDelegate next = _ =>
        {
            nextCalledCount++;
            return Task.CompletedTask;
        };

        var options = Options.Create(new ContextifyGatewayRateLimitOptionsEntity
        {
            Enabled = true,
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.TenantTool,
                permitLimit: 1,
                windowMs: 60000,
                queueLimit: 0)
        });
        var tenantOptions = Options.Create(new ContextifyGatewayTenantResolutionOptionsEntity());
        var logger = Mock.Of<ILogger<ContextifyGatewayRateLimitMiddleware>>();

        var middleware = new ContextifyGatewayRateLimitMiddleware(next, logger, options, tenantOptions);

        // Act - Tenant A calls tool1 once (should succeed)
        var contextA1 = CreateHttpContextWithTenant("/mcp", "tenant-a", CreateToolsCallJsonRpc("tool1"));
        await middleware.InvokeAsync(contextA1);

        // Tenant A calls tool1 again (should be rate limited)
        var contextA2 = CreateHttpContextWithTenant("/mcp", "tenant-a", CreateToolsCallJsonRpc("tool1"));
        await middleware.InvokeAsync(contextA2);

        // Tenant A calls tool2 (should succeed - different tool)
        var contextA3 = CreateHttpContextWithTenant("/mcp", "tenant-a", CreateToolsCallJsonRpc("tool2"));
        await middleware.InvokeAsync(contextA3);

        // Tenant B calls tool1 (should succeed - different tenant)
        var contextB1 = CreateHttpContextWithTenant("/mcp", "tenant-b", CreateToolsCallJsonRpc("tool1"));
        await middleware.InvokeAsync(contextB1);

        // Assert
        nextCalledCount.Should().Be(3); // A-tool1, A-tool2, B-tool1
        contextA1.Response.StatusCode.Should().Be(200);
        contextA2.Response.StatusCode.Should().Be(429); // Rate limited
        contextA3.Response.StatusCode.Should().Be(200);
        contextB1.Response.StatusCode.Should().Be(200);
    }

    #endregion

    #region Anonymous Tenant Tests

    /// <summary>
    /// Tests that missing tenant header maps to anonymous key but still rate limits.
    /// Requests without tenant headers should be rate limited under the "anonymous" tenant.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_MissingTenantHeader_MapsToAnonymousAndRateLimits()
    {
        // Arrange
        var nextCalledCount = 0;
        RequestDelegate next = _ =>
        {
            nextCalledCount++;
            return Task.CompletedTask;
        };

        var options = Options.Create(new ContextifyGatewayRateLimitOptionsEntity
        {
            Enabled = true,
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Tenant,
                permitLimit: 2,
                windowMs: 60000,
                queueLimit: 0)
        });
        var tenantOptions = Options.Create(new ContextifyGatewayTenantResolutionOptionsEntity
        {
            TenantHeaderName = "X-Tenant-Id"
        });
        var logger = Mock.Of<ILogger<ContextifyGatewayRateLimitMiddleware>>();

        var middleware = new ContextifyGatewayRateLimitMiddleware(next, logger, options, tenantOptions);

        // Act - Anonymous makes 2 requests (should succeed)
        var context1 = CreateHttpContext("/mcp", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(context1);

        var context2 = CreateHttpContext("/mcp", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(context2);

        // Anonymous makes 3rd request (should be rate limited)
        var context3 = CreateHttpContext("/mcp", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(context3);

        // Assert
        nextCalledCount.Should().Be(2);
        context1.Response.StatusCode.Should().Be(200);
        context2.Response.StatusCode.Should().Be(200);
        context3.Response.StatusCode.Should().Be(429); // Rate limited
    }

    /// <summary>
    /// Tests that anonymous and named tenants have independent quotas.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_AnonymousAndNamedTenants_HaveIndependentQuotas()
    {
        // Arrange
        var nextCalledCount = 0;
        RequestDelegate next = _ =>
        {
            nextCalledCount++;
            return Task.CompletedTask;
        };

        var options = Options.Create(new ContextifyGatewayRateLimitOptionsEntity
        {
            Enabled = true,
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Tenant,
                permitLimit: 1,
                windowMs: 60000,
                queueLimit: 0)
        });
        var tenantOptions = Options.Create(new ContextifyGatewayTenantResolutionOptionsEntity());
        var logger = Mock.Of<ILogger<ContextifyGatewayRateLimitMiddleware>>();

        var middleware = new ContextifyGatewayRateLimitMiddleware(next, logger, options, tenantOptions);

        // Act - Anonymous makes 1 request (should succeed)
        var contextAnon = CreateHttpContext("/mcp", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(contextAnon);

        // Anonymous makes 2nd request (should be rate limited)
        var contextAnon2 = CreateHttpContext("/mcp", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(contextAnon2);

        // Named tenant makes 1 request (should succeed - independent quota)
        var contextNamed = CreateHttpContextWithTenant("/mcp", "tenant-a", CreateToolsCallJsonRpc("test.tool"));
        await middleware.InvokeAsync(contextNamed);

        // Assert
        nextCalledCount.Should().Be(2); // anonymous, named
        contextAnon.Response.StatusCode.Should().Be(200);
        contextAnon2.Response.StatusCode.Should().Be(429); // Rate limited
        contextNamed.Response.StatusCode.Should().Be(200);
    }

    #endregion

    #region Non-Tools-Call Request Tests

    /// <summary>
    /// Tests that non-tools/call MCP methods bypass rate limiting.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_NonToolsCallRequest_BypassesRateLimiting()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var options = Options.Create(new ContextifyGatewayRateLimitOptionsEntity
        {
            Enabled = true,
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Global,
                permitLimit: 1,
                windowMs: 60000)
        });
        var tenantOptions = Options.Create(new ContextifyGatewayTenantResolutionOptionsEntity());
        var logger = Mock.Of<ILogger<ContextifyGatewayRateLimitMiddleware>>();

        var middleware = new ContextifyGatewayRateLimitMiddleware(next, logger, options, tenantOptions);

        // Create a tools/list request (not tools/call)
        var toolsListJson = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "tools/list",
            id = 1
        });

        var context = CreateHttpContext("/mcp", toolsListJson);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    #endregion

    #region Tool Override Pattern Tests

    /// <summary>
    /// Tests that tool-specific overrides apply correctly using wildcard patterns.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WithToolOverride_AppliesOverridePolicy()
    {
        // Arrange
        var nextCalledCount = 0;
        RequestDelegate next = _ =>
        {
            nextCalledCount++;
            return Task.CompletedTask;
        };

        var options = Options.Create(new ContextifyGatewayRateLimitOptionsEntity
        {
            Enabled = true,
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Global,
                permitLimit: 100,
                windowMs: 60000)
        });
        options.Value.SetOverrides(new Dictionary<string, ContextifyGatewayQuotaPolicyDto>
        {
            ["weather.*"] = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.TenantTool,
                permitLimit: 1,
                windowMs: 60000,
                queueLimit: 0)
        });

        var tenantOptions = Options.Create(new ContextifyGatewayTenantResolutionOptionsEntity());
        var logger = Mock.Of<ILogger<ContextifyGatewayRateLimitMiddleware>>();

        var middleware = new ContextifyGatewayRateLimitMiddleware(next, logger, options, tenantOptions);

        // Act - weather tool called once (should succeed with strict override)
        var context1 = CreateHttpContextWithTenant("/mcp", "tenant-a", CreateToolsCallJsonRpc("weather.get_forecast"));
        await middleware.InvokeAsync(context1);

        // weather tool called again (should be rate limited due to override)
        var context2 = CreateHttpContextWithTenant("/mcp", "tenant-a", CreateToolsCallJsonRpc("weather.get_forecast"));
        await middleware.InvokeAsync(context2);

        // Different tool called (should succeed - uses default policy)
        var context3 = CreateHttpContextWithTenant("/mcp", "tenant-a", CreateToolsCallJsonRpc("other.tool"));
        await middleware.InvokeAsync(context3);

        // Assert
        nextCalledCount.Should().Be(2); // weather1, other
        context1.Response.StatusCode.Should().Be(200);
        context2.Response.StatusCode.Should().Be(429); // Rate limited by override
        context3.Response.StatusCode.Should().Be(200);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates an HTTP context with the specified path and request body.
    /// </summary>
    private static HttpContext CreateHttpContext(string path, string? body = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.Request.Method = "POST";

        if (body != null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            httpContext.Request.Body = new MemoryStream(bytes);
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.ContentLength = bytes.Length;
        }

        return httpContext;
    }

    /// <summary>
    /// Creates an HTTP context with a tenant header.
    /// </summary>
    private static HttpContext CreateHttpContextWithTenant(
        string path,
        string tenantId,
        string? body = null)
    {
        var context = CreateHttpContext(path, body);
        context.Request.Headers["X-Tenant-Id"] = tenantId;
        return context;
    }

    /// <summary>
    /// Creates a JSON-RPC tools/call request body.
    /// </summary>
    private static string CreateToolsCallJsonRpc(string toolName)
    {
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            id = 1,
            @params = new
            {
                name = toolName,
                arguments = new { }
            }
        });
    }

    #endregion
}
