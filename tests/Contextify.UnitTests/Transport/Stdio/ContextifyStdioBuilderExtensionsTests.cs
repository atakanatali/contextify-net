using Contextify.Core;
using Contextify.Core.Extensions;
using Contextify.Core.Builder;
using Contextify.Core.Options;
using Contextify.Transport.Stdio;
using Contextify.Transport.Stdio.Extensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Contextify.UnitTests.Transport.Stdio;

/// <summary>
/// Unit tests for ContextifyStdioBuilderExtensions.
/// Verifies STDIO transport registration and conditional service registration.
/// </summary>
public sealed class ContextifyStdioBuilderExtensionsTests
{

    /// <summary>
    /// Tests that ConfigureStdio registers hosted service.
    /// </summary>
    [Fact]
    public void ConfigureStdio_RegistersHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddContextify(options => options.TransportMode = ContextifyTransportMode.Stdio)
                .ConfigureStdio();

        // Assert
        var hostedServiceDescriptors = services.Where(d =>
            d.ServiceType == typeof(ContextifyStdioHostedService) ||
            d.ImplementationType == typeof(ContextifyStdioHostedService));

        hostedServiceDescriptors.Should().NotBeEmpty("STDIO hosted service should be registered");
    }

    /// <summary>
    /// Tests that ConfigureStdio with null builder throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void ConfigureStdio_NullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        IContextifyBuilder? nullBuilder = null;

        // Act
        Action act = () => nullBuilder!.ConfigureStdio();

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("builder");
    }

    /// <summary>
    /// Tests that ConfigureStdio supports chaining with other builder methods.
    /// </summary>
    [Fact]
    public void ConfigureStdio_SupportsFluentChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = services.AddContextify(options => options.TransportMode = ContextifyTransportMode.Stdio)
            .ConfigureStdio();

        // Assert
        builder.Should().NotBeNull();
        builder.Services.Should().NotBeEmpty();
    }

    /// <summary>
    /// Tests that TryAdd prevents duplicate registrations.
    /// </summary>
    [Fact]
    public void ConfigureStdio_CalledMultipleTimes_DoesNotDuplicateRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddContextify()
                .ConfigureStdio()
                .ConfigureStdio();

        // Assert
        var handlerCount = services.Count(d =>
            d.ServiceType == typeof(Contextify.Transport.Stdio.JsonRpc.IContextifyStdioJsonRpcHandler));

        handlerCount.Should().Be(1, "TryAdd should prevent duplicate handler registrations");
    }
}

