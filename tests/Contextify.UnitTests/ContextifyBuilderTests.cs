using Contextify.Core;
using Contextify.Core.Builder;
using Contextify.Core.Extensions;
using Contextify.Core.Options;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Contextify.UnitTests;

/// <summary>
/// Unit tests for Contextify service registration and builder functionality.
/// Verifies that AddContextify correctly registers all required services and options.
/// </summary>
public sealed class ContextifyBuilderTests
{

    /// <summary>
    /// Tests that AddContextify registers ContextifyOptionsEntity service successfully.
    /// </summary>
    [Fact]
    public void AddContextify_WhenCalled_RegistersContextifyOptionsEntity()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddContextify();
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<ContextifyOptionsEntity>();

        // Assert
        options.Should().NotBeNull("ContextifyOptionsEntity should be registered");
        options!.TransportMode.Should().Be(ContextifyTransportMode.Auto, "default transport mode should be Auto");
        options.IsEnabled.Should().BeTrue("Contextify should be enabled by default");
    }

    /// <summary>
    /// Tests that AddContextify registers ContextifyLoggingOptionsEntity service successfully.
    /// </summary>
    [Fact]
    public void AddContextify_WhenCalled_RegistersContextifyLoggingOptionsEntity()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddContextify();
        var provider = services.BuildServiceProvider();
        var loggingOptions = provider.GetService<ContextifyLoggingOptionsEntity>();

        // Assert
        loggingOptions.Should().NotBeNull("ContextifyLoggingOptionsEntity should be registered");
        loggingOptions!.LogToolInvocations.Should().BeTrue("tool invocation logging should be enabled by default");
        loggingOptions.IncludeScopes.Should().BeTrue("scopes should be included by default");
    }

    /// <summary>
    /// Tests that AddContextify registers ContextifyPolicyOptionsEntity service successfully.
    /// </summary>
    [Fact]
    public void AddContextify_WhenCalled_RegistersContextifyPolicyOptionsEntity()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddContextify();
        var provider = services.BuildServiceProvider();
        var policyOptions = provider.GetService<ContextifyPolicyOptionsEntity>();

        // Assert
        policyOptions.Should().NotBeNull("ContextifyPolicyOptionsEntity should be registered");
        policyOptions!.AllowByDefault.Should().BeFalse("deny-by-default policy should be enabled for security");
        policyOptions.EnableRateLimiting.Should().BeTrue("rate limiting should be enabled by default");
        policyOptions.ValidateArguments.Should().BeTrue("argument validation should be enabled by default");
    }

    /// <summary>
    /// Tests that AddContextify registers ContextifyActionsOptionsEntity service successfully.
    /// </summary>
    [Fact]
    public void AddContextify_WhenCalled_RegistersContextifyActionsOptionsEntity()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddContextify();
        var provider = services.BuildServiceProvider();
        var actionsOptions = provider.GetService<ContextifyActionsOptionsEntity>();

        // Assert
        actionsOptions.Should().NotBeNull("ContextifyActionsOptionsEntity should be registered");
        actionsOptions!.EnableDefaultMiddleware.Should().BeTrue("default middleware should be enabled");
        actionsOptions.EnableValidation.Should().BeTrue("validation should be enabled by default");
        actionsOptions.EnableMetrics.Should().BeTrue("metrics should be enabled by default");
    }

    /// <summary>
    /// Tests that AddContextify with null services throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void AddContextify_WhenServicesIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection? nullServices = null;

        // Act
        Action act = () => nullServices!.AddContextify();

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services")
            .WithMessage("*cannot be null*");
    }

    /// <summary>
    /// Tests that AddContextify with configuration delegate applies the configuration.
    /// </summary>
    [Fact]
    public void AddContextify_WithConfigurationDelegate_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var expectedAppName = "TestApp";
        var expectedVersion = "1.0.0";
        var expectedTransportMode = ContextifyTransportMode.Http;

        // Act
        services.AddContextify(options =>
        {
            options.ApplicationName = expectedAppName;
            options.ApplicationVersion = expectedVersion;
            options.TransportMode = expectedTransportMode;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetService<ContextifyOptionsEntity>();

        // Assert
        options.Should().NotBeNull();
        options!.ApplicationName.Should().Be(expectedAppName);
        options.ApplicationVersion.Should().Be(expectedVersion);
        options.TransportMode.Should().Be(expectedTransportMode);
    }



    /// <summary>
    /// Tests that ContextifyOptionsEntity Validate throws for invalid timeout.
    /// </summary>
    [Fact]
    public void ContextifyOptionsEntity_Validate_WhenTimeoutIsZero_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyOptionsEntity
        {
            Actions = new ContextifyActionsOptionsEntity
            {
                DefaultExecutionTimeoutSeconds = 0
            }
        };

        // Act
        Action act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultExecutionTimeoutSeconds*must be greater than zero*");
    }

    /// <summary>
    /// Tests that ContextifyOptionsEntity Clone creates independent copy.
    /// </summary>
    [Fact]
    public void ContextifyOptionsEntity_Clone_CreatesIndependentCopy()
    {
        // Arrange
        var original = new ContextifyOptionsEntity
        {
            ApplicationName = "OriginalApp",
            TransportMode = ContextifyTransportMode.Http
        };
        original.Logging!.EnableDetailedLogging = true;
        original.Policy!.AllowedTools.Add("test:*");
        original.Actions!.EnableCaching = true;

        // Act
        var clone = original.Clone();

        // Modify clone
        clone.ApplicationName = "ClonedApp";
        clone.TransportMode = ContextifyTransportMode.Stdio;
        clone.Logging!.EnableDetailedLogging = false;
        clone.Policy!.AllowedTools.Add("other:*");
        clone.Actions!.EnableCaching = false;

        // Assert - original should be unchanged
        original.ApplicationName.Should().Be("OriginalApp");
        original.TransportMode.Should().Be(ContextifyTransportMode.Http);
        original.Logging!.EnableDetailedLogging.Should().BeTrue();
        original.Policy!.AllowedTools.Should().HaveCount(1, "original policy should not be modified");
        original.Actions!.EnableCaching.Should().BeTrue();

        // Clone should have modified values
        clone.ApplicationName.Should().Be("ClonedApp");
        clone.TransportMode.Should().Be(ContextifyTransportMode.Stdio);
        clone.Logging!.EnableDetailedLogging.Should().BeFalse();
        clone.Policy!.AllowedTools.Should().HaveCount(2);
        clone.Actions!.EnableCaching.Should().BeFalse();
    }
}
