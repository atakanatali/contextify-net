using Contextify.Gateway.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Gateway;

/// <summary>
/// Unit tests for ContextifyGatewayUpstreamEntity.
/// Verifies configuration entity initialization, validation, and cloning behavior.
/// </summary>
public sealed class ContextifyGatewayUpstreamEntityTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that the constructor creates an upstream with default values.
    /// </summary>
    [Fact]
    public void Constructor_WithDefaults_CreatesUpstream()
    {
        // Act
        var upstream = new ContextifyGatewayUpstreamEntity();

        // Assert
        upstream.Enabled.Should().BeTrue();
        upstream.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
        upstream.DefaultHeaders.Should().BeNull();
    }

    #endregion

    #region UpstreamName Tests

    /// <summary>
    /// Tests that UpstreamName can be set to a valid value.
    /// </summary>
    [Fact]
    public void UpstreamName_WithValidValue_SetsValue()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();
        var name = "test-upstream";

        // Act
        upstream.UpstreamName = name;

        // Assert
        upstream.UpstreamName.Should().Be(name);
    }

    /// <summary>
    /// Tests that UpstreamName throws when set to null.
    /// </summary>
    [Fact]
    public void UpstreamName_WhenSetToNull_ThrowsArgumentException()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();

        // Act
        var act = () => upstream.UpstreamName = null!;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Upstream name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that UpstreamName throws when set to empty string.
    /// </summary>
    [Fact]
    public void UpstreamName_WhenSetToEmpty_ThrowsArgumentException()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();

        // Act
        var act = () => upstream.UpstreamName = string.Empty;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Upstream name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that UpstreamName throws when set to whitespace.
    /// </summary>
    [Fact]
    public void UpstreamName_WhenSetToWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();

        // Act
        var act = () => upstream.UpstreamName = "   ";

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Upstream name cannot be null or whitespace*");
    }

    #endregion

    #region McpHttpEndpoint Tests

    /// <summary>
    /// Tests that McpHttpEndpoint can be set to a valid HTTP URI.
    /// </summary>
    [Fact]
    public void McpHttpEndpoint_WithHttpUri_SetsValue()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();
        var uri = new Uri("http://localhost:8080/mcp");

        // Act
        upstream.McpHttpEndpoint = uri;

        // Assert
        upstream.McpHttpEndpoint.Should().Be(uri);
    }

    /// <summary>
    /// Tests that McpHttpEndpoint can be set to a valid HTTPS URI.
    /// </summary>
    [Fact]
    public void McpHttpEndpoint_WithHttpsUri_SetsValue()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();
        var uri = new Uri("https://example.com/mcp");

        // Act
        upstream.McpHttpEndpoint = uri;

        // Assert
        upstream.McpHttpEndpoint.Should().Be(uri);
    }

    /// <summary>
    /// Tests that McpHttpEndpoint throws when set to null.
    /// </summary>
    [Fact]
    public void McpHttpEndpoint_WhenSetToNull_ThrowsArgumentNullException()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();

        // Act
        var act = () => upstream.McpHttpEndpoint = null!;

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("value");
    }

    /// <summary>
    /// Tests that McpHttpEndpoint throws when set to a relative URI.
    /// </summary>
    [Fact]
    public void McpHttpEndpoint_WhenSetToRelativeUri_ThrowsArgumentException()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();

        // Act
        var act = () => upstream.McpHttpEndpoint = new Uri("/mcp", UriKind.Relative);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*must be an absolute URI*");
    }

    /// <summary>
    /// Tests that McpHttpEndpoint throws when set to a URI with invalid scheme.
    /// </summary>
    [Theory]
    [InlineData("ftp://example.com/mcp")]
    [InlineData("file:///path/to/file")]
    [InlineData("ws://example.com/mcp")]
    [InlineData("wss://example.com/mcp")]
    public void McpHttpEndpoint_WhenSetToInvalidScheme_ThrowsArgumentException(string uriString)
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();
        var uri = new Uri(uriString);

        // Act
        var act = () => upstream.McpHttpEndpoint = uri;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*must be an absolute URI with HTTP or HTTPS scheme*");
    }

    #endregion

    #region NamespacePrefix Tests

    /// <summary>
    /// Tests that NamespacePrefix can be set to a valid value.
    /// </summary>
    [Fact]
    public void NamespacePrefix_WithValidValue_SetsValue()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();
        var prefix = "weather";

        // Act
        upstream.NamespacePrefix = prefix;

        // Assert
        upstream.NamespacePrefix.Should().Be(prefix);
    }

    /// <summary>
    /// Tests that NamespacePrefix throws when set to null.
    /// </summary>
    [Fact]
    public void NamespacePrefix_WhenSetToNull_ThrowsArgumentException()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();

        // Act
        var act = () => upstream.NamespacePrefix = null!;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Namespace prefix cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that NamespacePrefix throws when set to empty string.
    /// </summary>
    [Fact]
    public void NamespacePrefix_WhenSetToEmpty_ThrowsArgumentException()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();

        // Act
        var act = () => upstream.NamespacePrefix = string.Empty;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Namespace prefix cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that NamespacePrefix throws when set to a value with invalid characters.
    /// </summary>
    [Theory]
    [InlineData("invalid prefix")]
    [InlineData("invalid@prefix")]
    [InlineData("invalid/prefix")]
    [InlineData("invalid\\prefix")]
    [InlineData("invalid:prefix")]
    public void NamespacePrefix_WhenSetWithInvalidCharacters_ThrowsArgumentException(string invalidPrefix)
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();

        // Act
        var act = () => upstream.NamespacePrefix = invalidPrefix;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage($"*Namespace prefix '{invalidPrefix}' contains invalid characters*");
    }

    /// <summary>
    /// Tests that NamespacePrefix accepts valid characters.
    /// </summary>
    [Theory]
    [InlineData("valid")]
    [InlineData("valid123")]
    [InlineData("valid_name")]
    [InlineData("valid-name")]
    [InlineData("valid.name")]
    [InlineData("a")]
    [InlineData("ABC123_test-v1.final")]
    public void NamespacePrefix_WithValidCharacters_SetsValue(string validPrefix)
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();

        // Act
        var act = () => upstream.NamespacePrefix = validPrefix;

        // Assert
        act.Should().NotThrow();
        upstream.NamespacePrefix.Should().Be(validPrefix);
    }

    #endregion

    #region RequestTimeout Tests

    /// <summary>
    /// Tests that RequestTimeout can be set to a valid value.
    /// </summary>
    [Fact]
    public void RequestTimeout_WithValidValue_SetsValue()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();
        var timeout = TimeSpan.FromMinutes(5);

        // Act
        upstream.RequestTimeout = timeout;

        // Assert
        upstream.RequestTimeout.Should().Be(timeout);
    }

    #endregion

    #region DefaultHeaders Tests

    /// <summary>
    /// Tests that DefaultHeaders can be set to a valid dictionary.
    /// </summary>
    [Fact]
    public void DefaultHeaders_WithValidDictionary_SetsValue()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer token",
            ["Content-Type"] = "application/json"
        };

        // Act
        upstream.DefaultHeaders = headers;

        // Assert
        upstream.DefaultHeaders.Should().BeEquivalentTo(headers);
    }

    /// <summary>
    /// Tests that DefaultHeaders can be set to null.
    /// </summary>
    [Fact]
    public void DefaultHeaders_WhenSetToNull_SetsValue()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity();
        var headers = new Dictionary<string, string>();
        upstream.DefaultHeaders = headers;

        // Act
        upstream.DefaultHeaders = null;

        // Assert
        upstream.DefaultHeaders.Should().BeNull();
    }

    #endregion

    #region Validate Tests

    /// <summary>
    /// Tests that Validate passes for a valid upstream configuration.
    /// </summary>
    [Fact]
    public void Validate_WhenConfigurationIsValid_DoesNotThrow()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = "test-upstream",
            McpHttpEndpoint = new Uri("https://example.com/mcp"),
            NamespacePrefix = "test",
            Enabled = true,
            RequestTimeout = TimeSpan.FromSeconds(30)
        };

        // Act
        var act = () => upstream.Validate();

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that Validate throws when UpstreamName is empty.
    /// </summary>
    [Fact]
    public void Validate_WhenUpstreamNameIsEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity
        {
            McpHttpEndpoint = new Uri("https://example.com/mcp"),
            NamespacePrefix = "test"
        };

        // Act
        var act = () => upstream.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UpstreamName*cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that Validate throws when McpHttpEndpoint is null.
    /// </summary>
    [Fact]
    public void Validate_WhenMcpHttpEndpointIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = "test",
            NamespacePrefix = "test"
        };

        // Act
        var act = () => upstream.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*McpHttpEndpoint*cannot be null*");
    }

    /// <summary>
    /// Tests that Validate throws when RequestTimeout is zero or negative.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WhenRequestTimeoutIsZeroOrNegative_ThrowsInvalidOperationException(int seconds)
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = "test",
            McpHttpEndpoint = new Uri("https://example.com/mcp"),
            NamespacePrefix = "test",
            RequestTimeout = TimeSpan.FromSeconds(seconds)
        };

        // Act
        var act = () => upstream.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RequestTimeout*must be greater than zero*");
    }

    /// <summary>
    /// Tests that Validate throws when NamespacePrefix is empty.
    /// </summary>
    [Fact]
    public void Validate_WhenNamespacePrefixIsEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        var upstream = new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = "test",
            McpHttpEndpoint = new Uri("https://example.com/mcp")
        };

        // Act
        var act = () => upstream.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NamespacePrefix*cannot be null or whitespace*");
    }

    #endregion

    #region Clone Tests

    /// <summary>
    /// Tests that Clone creates an independent copy of the upstream.
    /// </summary>
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var original = new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = "test-upstream",
            McpHttpEndpoint = new Uri("https://example.com/mcp"),
            NamespacePrefix = "test",
            Enabled = true,
            RequestTimeout = TimeSpan.FromSeconds(45),
            DefaultHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer token"
            }
        };

        // Act
        var clone = original.Clone();

        // Assert
        clone.Should().NotBeSameAs(original);
        clone.UpstreamName.Should().Be(original.UpstreamName);
        clone.McpHttpEndpoint.Should().Be(original.McpHttpEndpoint);
        clone.NamespacePrefix.Should().Be(original.NamespacePrefix);
        clone.Enabled.Should().Be(original.Enabled);
        clone.RequestTimeout.Should().Be(original.RequestTimeout);
        clone.DefaultHeaders.Should().BeEquivalentTo(original.DefaultHeaders);
    }

    /// <summary>
    /// Tests that modifying the clone does not affect the original.
    /// </summary>
    [Fact]
    public void Clone_ModifyingCloneDoesNotAffectOriginal()
    {
        // Arrange
        var original = new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = "test-upstream",
            McpHttpEndpoint = new Uri("https://example.com/mcp"),
            NamespacePrefix = "test",
            Enabled = true,
            RequestTimeout = TimeSpan.FromSeconds(30),
            DefaultHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer token"
            }
        };

        // Act
        var clone = original.Clone();
        clone.UpstreamName = "modified";
        clone.Enabled = false;
        clone.RequestTimeout = TimeSpan.FromMinutes(10);

        if (clone.DefaultHeaders != null)
        {
            clone.DefaultHeaders["X-Custom"] = "value";
        }

        // Assert
        original.UpstreamName.Should().Be("test-upstream");
        original.Enabled.Should().BeTrue();
        original.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
        original.DefaultHeaders.Should().NotContainKey("X-Custom");
    }

    /// <summary>
    /// Tests that Clone handles null DefaultHeaders correctly.
    /// </summary>
    [Fact]
    public void Clone_WhenDefaultHeadersIsNull_CloneHasNullHeaders()
    {
        // Arrange
        var original = new ContextifyGatewayUpstreamEntity
        {
            UpstreamName = "test-upstream",
            McpHttpEndpoint = new Uri("https://example.com/mcp"),
            NamespacePrefix = "test",
            DefaultHeaders = null
        };

        // Act
        var clone = original.Clone();

        // Assert
        clone.DefaultHeaders.Should().BeNull();
    }

    #endregion
}
