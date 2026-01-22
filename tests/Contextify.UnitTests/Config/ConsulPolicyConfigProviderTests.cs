using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Contextify.Config.Abstractions.Policy;
using Contextify.Config.Consul.Options;
using Contextify.Config.Consul.Provider;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Contextify.UnitTests.Config;

public sealed class ConsulPolicyConfigProviderTests
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly ContextifyConsulOptionsEntity _options;
    private readonly Mock<ILogger<ConsulPolicyConfigProvider>> _loggerMock;
    private readonly ConsulPolicyConfigProvider _provider;

    public ConsulPolicyConfigProviderTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://consul:8500")
        };

        _options = new ContextifyConsulOptionsEntity
        {
            Address = "http://consul:8500",
            KeyPath = "contextify/policy",
            MinReloadIntervalMs = 500 // Minimum allowed value
        };
        var optionsMock = new Mock<IOptions<ContextifyConsulOptionsEntity>>();
        optionsMock.Setup(x => x.Value).Returns(_options);

        _loggerMock = new Mock<ILogger<ConsulPolicyConfigProvider>>();

        _provider = new ConsulPolicyConfigProvider(
            optionsMock.Object,
            _httpClient,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetAsync_SuccessfulFetch_ReturnsParsedConfig()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            Whitelist = new List<ContextifyEndpointPolicyDto> 
            { 
                new() { ToolName = "test", OperationId = "op", Enabled = true } 
            }
        };
        var json = JsonSerializer.Serialize(config);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var consulResponse = new[]
        {
            new { ModifyIndex = "123", Value = base64 }
        };

        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(consulResponse)
            });

        // Act
        var result = await _provider.GetAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.SourceVersion.Should().Be("123");
        result.Whitelist.Should().HaveCount(1);
        result.Whitelist[0].ToolName.Should().Be("test");
    }

    [Fact]
    public async Task GetAsync_KeyNotFound_ReturnsDefaultAllowByDefault()
    {
        // Arrange
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound
            });

        // Act
        var result = await _provider.GetAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.SourceVersion.Should().Be("0");
        result.DenyByDefault.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_InvalidBase64_UsesCachedConfigOrThrows()
    {
        // Arrange
        var consulResponse = new[]
        {
            new { ModifyIndex = "124", Value = "invalid-base64-!!!" }
        };

        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(consulResponse)
            });

        // Act & Assert
        var act = () => _provider.GetAsync(CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to decode Base64*");
    }


    [Fact]
    public void Watch_ReturnsChangeToken()
    {
        // Act
        var token = _provider.Watch();

        // Assert
        token.Should().NotBeNull();
        token!.HasChanged.Should().BeFalse();
    }
}
