using Contextify.Gateway.Core.Services;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Gateway;

/// <summary>
/// Unit tests for ContextifyGatewayToolNameService.
/// Verifies deterministic tool name translation, namespace prefix validation, and separator customization.
/// </summary>
public sealed class ContextifyGatewayToolNameServiceTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that the constructor creates a service with default separator.
    /// </summary>
    [Fact]
    public void Constructor_WithDefaultSeparator_CreatesService()
    {
        // Act
        var service = new ContextifyGatewayToolNameService();

        // Assert
        service.Separator.Should().Be(".");
    }

    /// <summary>
    /// Tests that the constructor creates a service with custom separator.
    /// </summary>
    [Fact]
    public void Constructor_WithCustomSeparator_CreatesService()
    {
        // Arrange
        var customSeparator = "-";

        // Act
        var service = new ContextifyGatewayToolNameService(customSeparator);

        // Assert
        service.Separator.Should().Be(customSeparator);
    }

    /// <summary>
    /// Tests that constructor throws when separator is null.
    /// </summary>
    [Fact]
    public void Constructor_WhenSeparatorIsNull_ThrowsArgumentException()
    {
        // Act
        var act = () => new ContextifyGatewayToolNameService(null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("separator")
            .WithMessage("*Separator cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that constructor throws when separator is empty string.
    /// </summary>
    [Fact]
    public void Constructor_WhenSeparatorIsEmpty_ThrowsArgumentException()
    {
        // Act
        var act = () => new ContextifyGatewayToolNameService(string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("separator")
            .WithMessage("*Separator cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that constructor throws when separator is whitespace.
    /// </summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("\t")]
    public void Constructor_WhenSeparatorIsWhitespace_ThrowsArgumentException(string separator)
    {
        // Act
        var act = () => new ContextifyGatewayToolNameService(separator);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("separator")
            .WithMessage("*Separator cannot be null or whitespace*");
    }

    #endregion

    #region ToExternalName Tests

    /// <summary>
    /// Tests that ToExternalName creates correct external name with default separator.
    /// </summary>
    [Fact]
    public void ToExternalName_WithDefaultSeparator_CreatesCorrectExternalName()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var namespacePrefix = "weather";
        var upstreamToolName = "get_forecast";

        // Act
        var externalName = service.ToExternalName(namespacePrefix, upstreamToolName);

        // Assert
        externalName.Should().Be("weather.get_forecast");
    }

    /// <summary>
    /// Tests that ToExternalName always prefixes even when upstream tool name contains separator.
    /// </summary>
    [Fact]
    public void ToExternalName_WhenUpstreamToolNameContainsSeparator_PrefixesAnyway()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var namespacePrefix = "analytics";
        var upstreamToolName = "data.query"; // Contains the separator

        // Act
        var externalName = service.ToExternalName(namespacePrefix, upstreamToolName);

        // Assert
        // Should be "analytics.data.query", not "analytics.analytics.data.query" or just "data.query"
        externalName.Should().Be("analytics.data.query");
    }

    /// <summary>
    /// Tests that ToExternalName uses custom separator correctly.
    /// </summary>
    [Fact]
    public void ToExternalName_WithCustomSeparator_UsesCustomSeparator()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService("-");
        var namespacePrefix = "weather";
        var upstreamToolName = "get_forecast";

        // Act
        var externalName = service.ToExternalName(namespacePrefix, upstreamToolName);

        // Assert
        externalName.Should().Be("weather-get_forecast");
    }

    /// <summary>
    /// Tests that ToExternalName throws when namespace prefix is null.
    /// </summary>
    [Fact]
    public void ToExternalName_WhenNamespacePrefixIsNull_ThrowsArgumentException()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var act = () => service.ToExternalName(null!, "tool_name");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("namespacePrefix")
            .WithMessage("*Namespace prefix cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that ToExternalName throws when namespace prefix is empty.
    /// </summary>
    [Fact]
    public void ToExternalName_WhenNamespacePrefixIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var act = () => service.ToExternalName(string.Empty, "tool_name");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("namespacePrefix")
            .WithMessage("*Namespace prefix cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that ToExternalName throws when upstream tool name is null.
    /// </summary>
    [Fact]
    public void ToExternalName_WhenUpstreamToolNameIsNull_ThrowsArgumentException()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var act = () => service.ToExternalName("namespace", null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("upstreamToolName")
            .WithMessage("*Upstream tool name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that ToExternalName throws when namespace prefix contains invalid characters.
    /// </summary>
    [Theory]
    [InlineData("invalid prefix")] // Space
    [InlineData("invalid@prefix")] // @ symbol
    [InlineData("invalid/prefix")] // Slash
    [InlineData("invalid\\prefix")] // Backslash
    [InlineData("invalid:prefix")] // Colon
    [InlineData("invalid;prefix")] // Semicolon
    [InlineData("invalid,prefix")] // Comma
    [InlineData("invalid!prefix")] // Exclamation
    [InlineData("invalid#prefix")] // Hash
    [InlineData("invalid$prefix")] // Dollar
    [InlineData("invalid%prefix")] // Percent
    [InlineData("invalid&prefix")] // Ampersand
    [InlineData("invalid*prefix")] // Asterisk
    [InlineData("invalid+prefix")] // Plus
    [InlineData("invalid=prefix")] // Equals
    [InlineData("invalid?prefix")] // Question mark
    [InlineData("invalid|prefix")] // Pipe
    [InlineData("invalid<prefix")] // Less than
    [InlineData("invalid>prefix")] // Greater than
    [InlineData("invalid[prefix")] // Open bracket
    [InlineData("invalid]prefix")] // Close bracket
    [InlineData("invalid{prefix")] // Open brace
    [InlineData("invalid}prefix")] // Close brace
    [InlineData("invalid(prefix")] // Open paren
    [InlineData("invalid)prefix")] // Close paren
    [InlineData("invalid`prefix")] // Backtick
    [InlineData("invalid'prefix")] // Single quote
    [InlineData("invalid\"prefix")] // Double quote
    public void ToExternalName_WhenNamespacePrefixHasInvalidCharacters_ThrowsArgumentException(string invalidPrefix)
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var act = () => service.ToExternalName(invalidPrefix, "tool_name");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("namespacePrefix")
            .WithMessage($"*Namespace prefix '{invalidPrefix}' contains invalid character*");
    }

    /// <summary>
    /// Tests that ToExternalName accepts valid namespace prefix characters.
    /// </summary>
    [Theory]
    [InlineData("valid")]
    [InlineData("Valid")]
    [InlineData("valid123")]
    [InlineData("valid_name")]
    [InlineData("valid-name")]
    [InlineData("valid.name")]
    [InlineData("valid_name-123.test")]
    [InlineData("a")]
    [InlineData("ABC123")]
    [InlineData("test.v1-final")]
    public void ToExternalName_WithValidNamespacePrefix_DoesNotThrow(string validPrefix)
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var act = () => service.ToExternalName(validPrefix, "tool_name");

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region ToInternalName Tests

    /// <summary>
    /// Tests that ToInternalName correctly extracts upstream tool name from external name.
    /// </summary>
    [Fact]
    public void ToInternalName_WithValidExternalName_ReturnsCorrectInternalName()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var upstreamName = "weather";
        var externalToolName = "weather.get_forecast";

        // Act
        var internalName = service.ToInternalName(upstreamName, externalToolName);

        // Assert
        internalName.Should().Be("get_forecast");
    }

    /// <summary>
    /// Tests that ToInternalName works when upstream tool name contains separator.
    /// </summary>
    [Fact]
    public void ToInternalName_WhenUpstreamToolNameContainsSeparator_ReturnsCorrectInternalName()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var upstreamName = "analytics";
        var externalToolName = "analytics.data.query";

        // Act
        var internalName = service.ToInternalName(upstreamName, externalToolName);

        // Assert
        internalName.Should().Be("data.query");
    }

    /// <summary>
    /// Tests that ToInternalName throws when upstream name is null.
    /// </summary>
    [Fact]
    public void ToInternalName_WhenUpstreamNameIsNull_ThrowsArgumentException()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var act = () => service.ToInternalName(null!, "namespace.tool_name");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("upstreamName")
            .WithMessage("*Upstream name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that ToInternalName throws when external tool name is null.
    /// </summary>
    [Fact]
    public void ToInternalName_WhenExternalToolNameIsNull_ThrowsArgumentException()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var act = () => service.ToInternalName("upstream", null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("externalToolName")
            .WithMessage("*External tool name cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that ToInternalName throws when external tool name doesn't start with expected prefix.
    /// </summary>
    [Fact]
    public void ToInternalName_WhenExternalToolNameDoesNotStartWithPrefix_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var upstreamName = "weather";
        var externalToolName = "analytics.get_forecast"; // Wrong prefix

        // Act
        var act = () => service.ToInternalName(upstreamName, externalToolName);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*External tool name '{externalToolName}' does not start with expected prefix 'weather.'*");
    }

    /// <summary>
    /// Tests that ToInternalName throws when external tool name results in empty internal name.
    /// </summary>
    [Fact]
    public void ToInternalName_WhenExternalToolNameIsJustPrefix_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var upstreamName = "weather";
        var externalToolName = "weather."; // Just the prefix with separator

        // Act
        var act = () => service.ToInternalName(upstreamName, externalToolName);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*results in an empty internal tool name*");
    }

    /// <summary>
    /// Tests that ToInternalName works with custom separator.
    /// </summary>
    [Fact]
    public void ToInternalName_WithCustomSeparator_ReturnsCorrectInternalName()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService("-");
        var upstreamName = "weather";
        var externalToolName = "weather-get_forecast";

        // Act
        var internalName = service.ToInternalName(upstreamName, externalToolName);

        // Assert
        internalName.Should().Be("get_forecast");
    }

    /// <summary>
    /// Tests that ToInternalName throws when upstream name has invalid characters.
    /// </summary>
    [Fact]
    public void ToInternalName_WhenUpstreamNameHasInvalidCharacters_ThrowsArgumentException()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var externalToolName = "invalid prefix.tool_name";

        // Act
        var act = () => service.ToInternalName("invalid prefix", externalToolName);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("namespacePrefix")
            .WithMessage("*contains invalid character*");
    }

    #endregion

    #region ToInternalNameByPrefix Tests

    /// <summary>
    /// Tests that ToInternalNameByPrefix correctly extracts upstream tool name.
    /// </summary>
    [Fact]
    public void ToInternalNameByPrefix_WithValidParameters_ReturnsCorrectInternalName()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var namespacePrefix = "weather";
        var externalToolName = "weather.get_forecast";

        // Act
        var internalName = service.ToInternalNameByPrefix(namespacePrefix, externalToolName);

        // Assert
        internalName.Should().Be("get_forecast");
    }

    /// <summary>
    /// Tests that ToInternalNameByPrefix works when namespace prefix differs from upstream name.
    /// </summary>
    [Fact]
    public void ToInternalNameByPrefix_WithCustomPrefix_ReturnsCorrectInternalName()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var namespacePrefix = "custom_weather";
        var externalToolName = "custom_weather.get_forecast";

        // Act
        var internalName = service.ToInternalNameByPrefix(namespacePrefix, externalToolName);

        // Assert
        internalName.Should().Be("get_forecast");
    }

    /// <summary>
    /// Tests that ToInternalNameByPrefix throws when namespace prefix is null.
    /// </summary>
    [Fact]
    public void ToInternalNameByPrefix_WhenNamespacePrefixIsNull_ThrowsArgumentException()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var act = () => service.ToInternalNameByPrefix(null!, "namespace.tool_name");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("namespacePrefix")
            .WithMessage("*Namespace prefix cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that ToInternalNameByPrefix throws when external tool name doesn't start with prefix.
    /// </summary>
    [Fact]
    public void ToInternalNameByPrefix_WhenExternalToolNameDoesNotStartWithPrefix_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var namespacePrefix = "weather";
        var externalToolName = "analytics.get_forecast";

        // Act
        var act = () => service.ToInternalNameByPrefix(namespacePrefix, externalToolName);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*does not start with expected prefix 'weather.'*");
    }

    #endregion

    #region Round-Trip Tests

    /// <summary>
    /// Tests that round-trip conversion (internal -> external -> internal) produces the original name.
    /// </summary>
    [Theory]
    [InlineData("weather", "get_forecast")]
    [InlineData("analytics", "data.query")]
    [InlineData("mcp-server", "tool.name")]
    [InlineData("test", "a")]
    [InlineData("prefix123", "tool_name-123.test")]
    public void RoundTrip_WithValidNames_ReturnsOriginalName(string namespacePrefix, string originalToolName)
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act - Convert to external and back
        var externalName = service.ToExternalName(namespacePrefix, originalToolName);
        var recoveredName = service.ToInternalNameByPrefix(namespacePrefix, externalName);

        // Assert
        recoveredName.Should().Be(originalToolName);
    }

    /// <summary>
    /// Tests that round-trip conversion works with custom separator.
    /// </summary>
    [Theory]
    [InlineData("-", "weather", "get_forecast")]
    [InlineData("--", "analytics", "data.query")]
    [InlineData("_", "test", "tool")]
    public void RoundTrip_WithCustomSeparator_ReturnsOriginalName(string separator, string namespacePrefix, string originalToolName)
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService(separator);

        // Act
        var externalName = service.ToExternalName(namespacePrefix, originalToolName);
        var recoveredName = service.ToInternalNameByPrefix(namespacePrefix, externalName);

        // Assert
        recoveredName.Should().Be(originalToolName);
    }

    /// <summary>
    /// Tests that external name format is deterministic for the same inputs.
    /// </summary>
    [Fact]
    public void ToExternalName_WithSameInputs_ProducesSameOutput()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var namespacePrefix = "weather";
        var upstreamToolName = "get_forecast";

        // Act
        var external1 = service.ToExternalName(namespacePrefix, upstreamToolName);
        var external2 = service.ToExternalName(namespacePrefix, upstreamToolName);

        // Assert
        external1.Should().Be(external2);
    }

    #endregion

    #region BelongsToUpstream Tests

    /// <summary>
    /// Tests that BelongsToUpstream returns true for matching prefix.
    /// </summary>
    [Fact]
    public void BelongsToUpstream_WithMatchingPrefix_ReturnsTrue()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var namespacePrefix = "weather";
        var externalToolName = "weather.get_forecast";

        // Act
        var result = service.BelongsToUpstream(namespacePrefix, externalToolName);

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// Tests that BelongsToUpstream returns false for non-matching prefix.
    /// </summary>
    [Fact]
    public void BelongsToUpstream_WithNonMatchingPrefix_ReturnsFalse()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var namespacePrefix = "analytics";
        var externalToolName = "weather.get_forecast";

        // Act
        var result = service.BelongsToUpstream(namespacePrefix, externalToolName);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that BelongsToUpstream returns false when namespace prefix is null.
    /// </summary>
    [Fact]
    public void BelongsToUpstream_WhenNamespacePrefixIsNull_ReturnsFalse()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var result = service.BelongsToUpstream(null!, "weather.get_forecast");

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that BelongsToUpstream returns false when external tool name is null.
    /// </summary>
    [Fact]
    public void BelongsToUpstream_WhenExternalToolNameIsNull_ReturnsFalse()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var result = service.BelongsToUpstream("weather", null!);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that BelongsToUpstream returns false when prefix is invalid.
    /// </summary>
    [Fact]
    public void BelongsToUpstream_WhenPrefixIsInvalid_ReturnsFalse()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var result = service.BelongsToUpstream("invalid prefix", "invalid prefix.tool_name");

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that BelongsToUpstream requires the separator after the prefix.
    /// </summary>
    [Fact]
    public void BelongsToUpstream_WhenNameStartsWithPrefixButNoSeparator_ReturnsFalse()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var namespacePrefix = "weather";
        var externalToolName = "weatherforecast"; // Starts with "weather" but no separator

        // Act
        var result = service.BelongsToUpstream(namespacePrefix, externalToolName);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region ExtractNamespacePrefix Tests

    /// <summary>
    /// Tests that ExtractNamespacePrefix correctly extracts the prefix.
    /// </summary>
    [Fact]
    public void ExtractNamespacePrefix_WithValidExternalName_ReturnsPrefix()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var externalToolName = "weather.get_forecast";

        // Act
        var prefix = service.ExtractNamespacePrefix(externalToolName);

        // Assert
        prefix.Should().Be("weather");
    }

    /// <summary>
    /// Tests that ExtractNamespacePrefix returns null when no separator is found.
    /// </summary>
    [Fact]
    public void ExtractNamespacePrefix_WhenNoSeparatorFound_ReturnsNull()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var externalToolName = "get_forecast";

        // Act
        var prefix = service.ExtractNamespacePrefix(externalToolName);

        // Assert
        prefix.Should().BeNull();
    }

    /// <summary>
    /// Tests that ExtractNamespacePrefix returns null when separator is at the start.
    /// </summary>
    [Fact]
    public void ExtractNamespacePrefix_WhenSeparatorAtStart_ReturnsNull()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var externalToolName = ".get_forecast";

        // Act
        var prefix = service.ExtractNamespacePrefix(externalToolName);

        // Assert
        prefix.Should().BeNull();
    }

    /// <summary>
    /// Tests that ExtractNamespacePrefix returns null for null input.
    /// </summary>
    [Fact]
    public void ExtractNamespacePrefix_WhenInputIsNull_ReturnsNull()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var prefix = service.ExtractNamespacePrefix(null!);

        // Assert
        prefix.Should().BeNull();
    }

    /// <summary>
    /// Tests that ExtractNamespacePrefix returns null for empty input.
    /// </summary>
    [Fact]
    public void ExtractNamespacePrefix_WhenInputIsEmpty_ReturnsNull()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var prefix = service.ExtractNamespacePrefix(string.Empty);

        // Assert
        prefix.Should().BeNull();
    }

    /// <summary>
    /// Tests that ExtractNamespacePrefix handles multiple separators correctly.
    /// Should extract up to the last separator occurrence.
    /// </summary>
    [Fact]
    public void ExtractNamespacePrefix_WithMultipleSeparators_ReturnsPrefixUpToLastSeparator()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var externalToolName = "analytics.data.query";

        // Act
        var prefix = service.ExtractNamespacePrefix(externalToolName);

        // Assert
        prefix.Should().Be("analytics.data");
    }

    /// <summary>
    /// Tests that ExtractNamespacePrefix returns null when prefix contains invalid characters.
    /// </summary>
    [Fact]
    public void ExtractNamespacePrefix_WhenPrefixHasInvalidCharacters_ReturnsNull()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();
        var externalToolName = "invalid prefix.get_forecast";

        // Act
        var prefix = service.ExtractNamespacePrefix(externalToolName);

        // Assert
        prefix.Should().BeNull();
    }

    /// <summary>
    /// Tests that ExtractNamespacePrefix works with custom separator.
    /// </summary>
    [Fact]
    public void ExtractNamespacePrefix_WithCustomSeparator_ReturnsCorrectPrefix()
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService("-");
        var externalToolName = "weather-get_forecast";

        // Act
        var prefix = service.ExtractNamespacePrefix(externalToolName);

        // Assert
        prefix.Should().Be("weather");
    }

    /// <summary>
    /// Tests that ExtractNamespacePrefix handles complex valid prefixes.
    /// </summary>
    [Theory]
    [InlineData("a.tool", "a")]
    [InlineData("ABC.tool", "ABC")]
    [InlineData("test123.tool", "test123")]
    [InlineData("test-name.tool", "test-name")]
    [InlineData("test_name.tool", "test_name")]
    [InlineData("test.name.tool", "test.name")]
    [InlineData("test-v1.final.tool", "test-v1.final")]
    public void ExtractNamespacePrefix_WithValidComplexPrefixes_ReturnsCorrectPrefix(string externalName, string expectedPrefix)
    {
        // Arrange
        var service = new ContextifyGatewayToolNameService();

        // Act
        var prefix = service.ExtractNamespacePrefix(externalName);

        // Assert
        prefix.Should().Be(expectedPrefix);
    }

    #endregion
}
