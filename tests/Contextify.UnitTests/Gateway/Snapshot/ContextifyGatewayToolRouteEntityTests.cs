using Contextify.Gateway.Core.Snapshot;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Gateway.Snapshot;

/// <summary>
/// Unit tests for ContextifyGatewayToolRouteEntity.
/// Verifies tool route immutability, validation, and deep copy behavior.
/// </summary>
public sealed class ContextifyGatewayToolRouteEntityTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that the constructor creates a tool route with correct values.
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_CreatesToolRoute()
    {
        // Arrange
        var externalToolName = "weather.get_forecast";
        var upstreamName = "weather";
        var upstreamToolName = "get_forecast";
        var schemaJson = "{\"type\":\"object\"}";
        var description = "Get weather forecast";

        // Act
        var route = new ContextifyGatewayToolRouteEntity(
            externalToolName,
            upstreamName,
            upstreamToolName,
            schemaJson,
            description);

        // Assert
        route.ExternalToolName.Should().Be(externalToolName);
        route.UpstreamName.Should().Be(upstreamName);
        route.UpstreamToolName.Should().Be(upstreamToolName);
        route.UpstreamInputSchemaJson.Should().Be(schemaJson);
        route.Description.Should().Be(description);
    }

    /// <summary>
    /// Tests that constructor creates a tool route with nullable fields set to null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullableNulls_CreatesToolRoute()
    {
        // Arrange
        var externalToolName = "analytics.query";
        var upstreamName = "analytics";
        var upstreamToolName = "query";

        // Act
        var route = new ContextifyGatewayToolRouteEntity(
            externalToolName,
            upstreamName,
            upstreamToolName,
            null,
            null);

        // Assert
        route.ExternalToolName.Should().Be(externalToolName);
        route.UpstreamName.Should().Be(upstreamName);
        route.UpstreamToolName.Should().Be(upstreamToolName);
        route.UpstreamInputSchemaJson.Should().BeNull();
        route.Description.Should().BeNull();
    }

    /// <summary>
    /// Tests that constructor throws when external tool name is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenExternalToolNameIsNull_ThrowsArgumentException()
    {
        // Act
        var act = () => new ContextifyGatewayToolRouteEntity(
            null!,
            "upstream",
            "tool",
            null,
            null);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("externalToolName")
            .WithMessage("*External tool name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that constructor throws when external tool name is empty.
    /// </summary>
    [Fact]
    public void Constructor_WhenExternalToolNameIsEmpty_ThrowsArgumentException()
    {
        // Act
        var act = () => new ContextifyGatewayToolRouteEntity(
            string.Empty,
            "upstream",
            "tool",
            null,
            null);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("externalToolName")
            .WithMessage("*External tool name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that constructor throws when upstream name is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenUpstreamNameIsNull_ThrowsArgumentException()
    {
        // Act
        var act = () => new ContextifyGatewayToolRouteEntity(
            "external.tool",
            null!,
            "tool",
            null,
            null);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("upstreamName")
            .WithMessage("*Upstream name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that constructor throws when upstream tool name is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenUpstreamToolNameIsNull_ThrowsArgumentException()
    {
        // Act
        var act = () => new ContextifyGatewayToolRouteEntity(
            "external.tool",
            "upstream",
            null!,
            null,
            null);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("upstreamToolName")
            .WithMessage("*Upstream tool name cannot be null or whitespace*");
    }

    #endregion

    #region DeepCopy Tests

    /// <summary>
    /// Tests that DeepCopy creates an independent copy of the tool route.
    /// </summary>
    [Fact]
    public void DeepCopy_CreatesIndependentCopy()
    {
        // Arrange
        var original = new ContextifyGatewayToolRouteEntity(
            "weather.get_forecast",
            "weather",
            "get_forecast",
            "{\"type\":\"object\"}",
            "Get weather forecast");

        // Act
        var copy = original.DeepCopy();

        // Assert
        copy.Should().NotBeSameAs(original);
        copy.ExternalToolName.Should().Be(original.ExternalToolName);
        copy.UpstreamName.Should().Be(original.UpstreamName);
        copy.UpstreamToolName.Should().Be(original.UpstreamToolName);
        copy.UpstreamInputSchemaJson.Should().Be(original.UpstreamInputSchemaJson);
        copy.Description.Should().Be(original.Description);
    }

    /// <summary>
    /// Tests that DeepCopy handles null schema and description correctly.
    /// </summary>
    [Fact]
    public void DeepCopy_WithNullables_CopiesCorrectly()
    {
        // Arrange
        var original = new ContextifyGatewayToolRouteEntity(
            "analytics.query",
            "analytics",
            "query",
            null,
            null);

        // Act
        var copy = original.DeepCopy();

        // Assert
        copy.UpstreamInputSchemaJson.Should().BeNull();
        copy.Description.Should().BeNull();
    }

    #endregion

    #region Validate Tests

    /// <summary>
    /// Tests that Validate succeeds for a valid tool route.
    /// </summary>
    [Fact]
    public void Validate_WhenToolRouteIsValid_DoesNotThrow()
    {
        // Arrange
        var route = new ContextifyGatewayToolRouteEntity(
            "weather.get_forecast",
            "weather",
            "get_forecast",
            "{\"type\":\"object\"}",
            "Get weather forecast");

        // Act
        var act = () => route.Validate();

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that Validate succeeds with nullable nulls.
    /// </summary>
    [Fact]
    public void Validate_WithNullableNulls_DoesNotThrow()
    {
        // Arrange
        var route = new ContextifyGatewayToolRouteEntity(
            "analytics.query",
            "analytics",
            "query",
            null,
            null);

        // Act
        var act = () => route.Validate();

        // Assert
        act.Should().NotThrow();
    }

    #endregion
}
