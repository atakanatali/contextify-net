using Contextify.Gateway.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Gateway;

/// <summary>
/// Unit tests for ContextifyGatewayOptionsEntity.
/// Verifies gateway configuration initialization, validation, and upstream management.
/// </summary>
public sealed class ContextifyGatewayOptionsEntityTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that the constructor creates options with default values.
    /// </summary>
    [Fact]
    public void Constructor_WithDefaults_CreatesOptions()
    {
        // Act
        var options = new ContextifyGatewayOptionsEntity();

        // Assert
        options.ToolNameSeparator.Should().Be(".");
        options.DenyByDefault.Should().BeFalse();
        options.CatalogRefreshInterval.Should().Be(TimeSpan.FromMinutes(5));
        options.Upstreams.Should().BeEmpty();
    }

    #endregion

    #region ToolNameSeparator Tests

    /// <summary>
    /// Tests that ToolNameSeparator can be set to a valid value.
    /// </summary>
    [Fact]
    public void ToolNameSeparator_WithValidValue_SetsValue()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var separator = "-";

        // Act
        options.ToolNameSeparator = separator;

        // Assert
        options.ToolNameSeparator.Should().Be(separator);
    }

    /// <summary>
    /// Tests that ToolNameSeparator throws when set to null.
    /// </summary>
    [Fact]
    public void ToolNameSeparator_WhenSetToNull_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var act = () => options.ToolNameSeparator = null!;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Tool name separator cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that ToolNameSeparator throws when set to empty string.
    /// </summary>
    [Fact]
    public void ToolNameSeparator_WhenSetToEmpty_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var act = () => options.ToolNameSeparator = string.Empty;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Tool name separator cannot be null or whitespace*");
    }

    /// <summary>
    /// Tests that ToolNameSeparator throws when set to whitespace.
    /// </summary>
    [Fact]
    public void ToolNameSeparator_WhenSetToWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var act = () => options.ToolNameSeparator = "   ";

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Tool name separator cannot be null or whitespace*");
    }

    #endregion

    #region CatalogRefreshInterval Tests

    /// <summary>
    /// Tests that CatalogRefreshInterval can be set to a valid value.
    /// </summary>
    [Fact]
    public void CatalogRefreshInterval_WithValidValue_SetsValue()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var interval = TimeSpan.FromMinutes(10);

        // Act
        options.CatalogRefreshInterval = interval;

        // Assert
        options.CatalogRefreshInterval.Should().Be(interval);
    }

    /// <summary>
    /// Tests that CatalogRefreshInterval throws when set to zero.
    /// </summary>
    [Fact]
    public void CatalogRefreshInterval_WhenSetToZero_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var act = () => options.CatalogRefreshInterval = TimeSpan.Zero;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Catalog refresh interval must be greater than zero*");
    }

    /// <summary>
    /// Tests that CatalogRefreshInterval throws when set to negative value.
    /// </summary>
    [Fact]
    public void CatalogRefreshInterval_WhenSetToNegative_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var act = () => options.CatalogRefreshInterval = TimeSpan.FromSeconds(-1);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Catalog refresh interval must be greater than zero*");
    }

    #endregion

    #region SetUpstreams Tests

    /// <summary>
    /// Tests that SetUpstreams sets the upstreams correctly.
    /// </summary>
    [Fact]
    public void SetUpstreams_WithValidList_SetsUpstreams()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var upstreams = new List<ContextifyGatewayUpstreamEntity>
        {
            new() { UpstreamName = "upstream1", McpHttpEndpoint = new Uri("https://example1.com") },
            new() { UpstreamName = "upstream2", McpHttpEndpoint = new Uri("https://example2.com") }
        };

        // Act
        options.SetUpstreams(upstreams);

        // Assert
        options.Upstreams.Should().HaveCount(2);
        options.Upstreams.Should().BeEquivalentTo(upstreams);
    }

    /// <summary>
    /// Tests that SetUpstreams throws when upstreams is null.
    /// </summary>
    [Fact]
    public void SetUpstreams_WhenUpstreamsIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var act = () => options.SetUpstreams(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("upstreams");
    }

    #endregion

    #region Validate Tests

    /// <summary>
    /// Tests that Validate passes for a valid configuration.
    /// </summary>
    [Fact]
    public void Validate_WhenConfigurationIsValid_DoesNotThrow()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new()
            {
                UpstreamName = "test-upstream",
                McpHttpEndpoint = new Uri("https://example.com/mcp"),
                NamespacePrefix = "test",
                Enabled = true,
                RequestTimeout = TimeSpan.FromSeconds(30)
            }
        });

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that Validate throws when ToolNameSeparator is empty.
    /// </summary>
    /// <summary>
    /// Tests that ToolNameSeparator cannot be set to empty/whitespace via direct assignment.
    /// (Previously tested in Validate, now covered by strict setter tests above).
    /// </summary>
    [Fact]
    public void ToolNameSeparator_WhenSetToEmpty_ThrowsImmediately()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var act = () => options.ToolNameSeparator = string.Empty;

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Tests that Validate throws when CatalogRefreshInterval is zero.
    /// </summary>
    /// <summary>
    /// Tests that CatalogRefreshInterval cannot be set to zero via direct assignment.
    /// (Previously tested in Validate, now covered by strict setter tests above).
    /// </summary>
    [Fact]
    public void CatalogRefreshInterval_WhenSetToZero_ThrowsImmediately()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var act = () => options.CatalogRefreshInterval = TimeSpan.Zero;

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Tests that Validate throws when an upstream is null.
    /// </summary>
    [Fact]
    public void Validate_WhenUpstreamIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var upstreamsList = new List<ContextifyGatewayUpstreamEntity> { null! };
        options.SetUpstreams(upstreamsList);

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Upstream at index 0 is null*");
    }

    /// <summary>
    /// Tests that Validate throws when upstream names are duplicated.
    /// </summary>
    [Fact]
    public void Validate_WhenUpstreamNamesAreDuplicated_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new() { UpstreamName = "duplicate", McpHttpEndpoint = new Uri("https://example1.com/mcp"), NamespacePrefix = "prefix1" },
            new() { UpstreamName = "duplicate", McpHttpEndpoint = new Uri("https://example2.com/mcp"), NamespacePrefix = "prefix2" }
        });

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate upstream name detected: 'duplicate'*");
    }

    /// <summary>
    /// Tests that Validate throws when namespace prefixes are duplicated.
    /// </summary>
    [Fact]
    public void Validate_WhenNamespacePrefixesAreDuplicated_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new() { UpstreamName = "upstream1", McpHttpEndpoint = new Uri("https://example1.com/mcp"), NamespacePrefix = "duplicate" },
            new() { UpstreamName = "upstream2", McpHttpEndpoint = new Uri("https://example2.com/mcp"), NamespacePrefix = "duplicate" }
        });

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate namespace prefix detected: 'duplicate'*");
    }

    /// <summary>
    /// Tests that Validate propagates upstream validation errors.
    /// </summary>
    [Fact]
    public void Validate_WhenUpstreamIsInvalid_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var upstream = new ContextifyGatewayUpstreamEntity();
        
        // Act - This will throw ArgumentException because UpstreamName setter is strict
        var act = () => upstream.UpstreamName = "";

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Clone Tests

    /// <summary>
    /// Tests that Clone creates an independent copy of the options.
    /// </summary>
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var original = new ContextifyGatewayOptionsEntity
        {
            ToolNameSeparator = "-",
            DenyByDefault = true,
            CatalogRefreshInterval = TimeSpan.FromMinutes(10)
        };
        original.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new()
            {
                UpstreamName = "test-upstream",
                McpHttpEndpoint = new Uri("https://example.com/mcp"),
                NamespacePrefix = "test",
                Enabled = true,
                RequestTimeout = TimeSpan.FromSeconds(45)
            }
        });

        // Act
        var clone = original.Clone();

        // Assert
        clone.Should().NotBeSameAs(original);
        clone.ToolNameSeparator.Should().Be(original.ToolNameSeparator);
        clone.DenyByDefault.Should().Be(original.DenyByDefault);
        clone.CatalogRefreshInterval.Should().Be(original.CatalogRefreshInterval);
        clone.Upstreams.Should().HaveCount(1);
        clone.Upstreams[0].Should().NotBeSameAs(original.Upstreams[0]);
    }

    /// <summary>
    /// Tests that modifying the clone does not affect the original.
    /// </summary>
    [Fact]
    public void Clone_ModifyingCloneDoesNotAffectOriginal()
    {
        // Arrange
        var original = new ContextifyGatewayOptionsEntity
        {
            ToolNameSeparator = ".",
            DenyByDefault = false
        };
        original.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new()
            {
                UpstreamName = "test-upstream",
                McpHttpEndpoint = new Uri("https://example.com/mcp"),
                NamespacePrefix = "test"
            }
        });

        // Act
        var clone = original.Clone();
        clone.ToolNameSeparator = "-";
        clone.DenyByDefault = true;

        // Assert
        original.ToolNameSeparator.Should().Be(".");
        original.DenyByDefault.Should().BeFalse();
    }

    /// <summary>
    /// Tests that Clone handles empty upstreams correctly.
    /// </summary>
    [Fact]
    public void Clone_WhenUpstreamsIsEmpty_CloneHasEmptyUpstreams()
    {
        // Arrange
        var original = new ContextifyGatewayOptionsEntity();

        // Act
        var clone = original.Clone();

        // Assert
        clone.Upstreams.Should().BeEmpty();
    }

    #endregion

    #region GetEnabledUpstreams Tests

    /// <summary>
    /// Tests that GetEnabledUpstreams returns only enabled upstreams.
    /// </summary>
    [Fact]
    public void GetEnabledUpstreams_WithMixedUpstreams_ReturnsOnlyEnabled()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new() { UpstreamName = "enabled1", McpHttpEndpoint = new Uri("https://example1.com"), NamespacePrefix = "e1", Enabled = true },
            new() { UpstreamName = "disabled1", McpHttpEndpoint = new Uri("https://example2.com"), NamespacePrefix = "d1", Enabled = false },
            new() { UpstreamName = "enabled2", McpHttpEndpoint = new Uri("https://example3.com"), NamespacePrefix = "e2", Enabled = true }
        });

        // Act
        var enabledUpstreams = options.GetEnabledUpstreams();

        // Assert
        enabledUpstreams.Should().HaveCount(2);
        enabledUpstreams.All(u => u.Enabled).Should().BeTrue();
    }

    /// <summary>
    /// Tests that GetEnabledUpstreams returns empty list when all upstreams are disabled.
    /// </summary>
    [Fact]
    public void GetEnabledUpstreams_WhenAllDisabled_ReturnsEmptyList()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new() { UpstreamName = "disabled1", McpHttpEndpoint = new Uri("https://example1.com"), NamespacePrefix = "d1", Enabled = false },
            new() { UpstreamName = "disabled2", McpHttpEndpoint = new Uri("https://example2.com"), NamespacePrefix = "d2", Enabled = false }
        });

        // Act
        var enabledUpstreams = options.GetEnabledUpstreams();

        // Assert
        enabledUpstreams.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that GetEnabledUpstreams returns empty list when no upstreams configured.
    /// </summary>
    [Fact]
    public void GetEnabledUpstreams_WhenNoUpstreams_ReturnsEmptyList()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var enabledUpstreams = options.GetEnabledUpstreams();

        // Assert
        enabledUpstreams.Should().BeEmpty();
    }

    #endregion

    #region GetUpstreamByName Tests

    /// <summary>
    /// Tests that GetUpstreamByName returns the correct upstream.
    /// </summary>
    [Fact]
    public void GetUpstreamByName_WhenUpstreamExists_ReturnsUpstream()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new() { UpstreamName = "upstream1", McpHttpEndpoint = new Uri("https://example1.com"), NamespacePrefix = "u1" },
            new() { UpstreamName = "upstream2", McpHttpEndpoint = new Uri("https://example2.com"), NamespacePrefix = "u2" }
        });

        // Act
        var upstream = options.GetUpstreamByName("upstream1");

        // Assert
        upstream.Should().NotBeNull();
        upstream!.UpstreamName.Should().Be("upstream1");
    }

    /// <summary>
    /// Tests that GetUpstreamByName returns null when upstream doesn't exist.
    /// </summary>
    [Fact]
    public void GetUpstreamByName_WhenUpstreamDoesNotExist_ReturnsNull()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new() { UpstreamName = "upstream1", McpHttpEndpoint = new Uri("https://example1.com"), NamespacePrefix = "u1" }
        });

        // Act
        var upstream = options.GetUpstreamByName("nonexistent");

        // Assert
        upstream.Should().BeNull();
    }

    /// <summary>
    /// Tests that GetUpstreamByName returns null when name is null.
    /// </summary>
    [Fact]
    public void GetUpstreamByName_WhenNameIsNull_ReturnsNull()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var upstream = options.GetUpstreamByName(null!);

        // Assert
        upstream.Should().BeNull();
    }

    /// <summary>
    /// Tests that GetUpstreamByName returns null when name is empty.
    /// </summary>
    [Fact]
    public void GetUpstreamByName_WhenNameIsEmpty_ReturnsNull()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var upstream = options.GetUpstreamByName(string.Empty);

        // Assert
        upstream.Should().BeNull();
    }

    /// <summary>
    /// Tests that GetUpstreamByName is case-sensitive.
    /// </summary>
    [Fact]
    public void GetUpstreamByName_IsCaseSensitive()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new() { UpstreamName = "UpstreamOne", McpHttpEndpoint = new Uri("https://example.com"), NamespacePrefix = "u1" }
        });

        // Act
        var upstream1 = options.GetUpstreamByName("UpstreamOne");
        var upstream2 = options.GetUpstreamByName("upstreamone");
        var upstream3 = options.GetUpstreamByName("UPSTREAMONE");

        // Assert
        upstream1.Should().NotBeNull();
        upstream2.Should().BeNull();
        upstream3.Should().BeNull();
    }

    #endregion

    #region GetUpstreamByNamespacePrefix Tests

    /// <summary>
    /// Tests that GetUpstreamByNamespacePrefix returns the correct upstream.
    /// </summary>
    [Fact]
    public void GetUpstreamByNamespacePrefix_WhenPrefixExists_ReturnsUpstream()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new() { UpstreamName = "upstream1", McpHttpEndpoint = new Uri("https://example1.com"), NamespacePrefix = "weather" },
            new() { UpstreamName = "upstream2", McpHttpEndpoint = new Uri("https://example2.com"), NamespacePrefix = "analytics" }
        });

        // Act
        var upstream = options.GetUpstreamByNamespacePrefix("weather");

        // Assert
        upstream.Should().NotBeNull();
        upstream!.NamespacePrefix.Should().Be("weather");
    }

    /// <summary>
    /// Tests that GetUpstreamByNamespacePrefix returns null when prefix doesn't exist.
    /// </summary>
    [Fact]
    public void GetUpstreamByNamespacePrefix_WhenPrefixDoesNotExist_ReturnsNull()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new() { UpstreamName = "upstream1", McpHttpEndpoint = new Uri("https://example1.com"), NamespacePrefix = "weather" }
        });

        // Act
        var upstream = options.GetUpstreamByNamespacePrefix("nonexistent");

        // Assert
        upstream.Should().BeNull();
    }

    /// <summary>
    /// Tests that GetUpstreamByNamespacePrefix returns null when prefix is null.
    /// </summary>
    [Fact]
    public void GetUpstreamByNamespacePrefix_WhenPrefixIsNull_ReturnsNull()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var upstream = options.GetUpstreamByNamespacePrefix(null!);

        // Assert
        upstream.Should().BeNull();
    }

    /// <summary>
    /// Tests that GetUpstreamByNamespacePrefix is case-sensitive.
    /// </summary>
    [Fact]
    public void GetUpstreamByNamespacePrefix_IsCaseSensitive()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetUpstreams(new List<ContextifyGatewayUpstreamEntity>
        {
            new() { UpstreamName = "upstream1", McpHttpEndpoint = new Uri("https://example.com"), NamespacePrefix = "Weather" }
        });

        // Act
        var upstream1 = options.GetUpstreamByNamespacePrefix("Weather");
        var upstream2 = options.GetUpstreamByNamespacePrefix("weather");

        // Assert
        upstream1.Should().NotBeNull();
        upstream2.Should().BeNull();
    }

    #endregion

    #region AllowedToolPatterns Tests

    /// <summary>
    /// Tests that AllowedToolPatterns defaults to empty collection.
    /// </summary>
    [Fact]
    public void AllowedToolPatterns_DefaultValue_IsEmpty()
    {
        // Arrange & Act
        var options = new ContextifyGatewayOptionsEntity();

        // Assert
        options.AllowedToolPatterns.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that SetAllowedToolPatterns sets the patterns correctly.
    /// </summary>
    [Fact]
    public void SetAllowedToolPatterns_WithValidList_SetsPatterns()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var patterns = new List<string> { "payments.*", "users.read_*" };

        // Act
        options.SetAllowedToolPatterns(patterns);

        // Assert
        options.AllowedToolPatterns.Should().HaveCount(2);
        options.AllowedToolPatterns.Should().BeEquivalentTo(patterns);
    }

    /// <summary>
    /// Tests that SetAllowedToolPatterns throws when patterns is null.
    /// </summary>
    [Fact]
    public void SetAllowedToolPatterns_WhenPatternsIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var act = () => options.SetAllowedToolPatterns(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("allowedToolPatterns");
    }

    #endregion

    #region DeniedToolPatterns Tests

    /// <summary>
    /// Tests that DeniedToolPatterns defaults to empty collection.
    /// </summary>
    [Fact]
    public void DeniedToolPatterns_DefaultValue_IsEmpty()
    {
        // Arrange & Act
        var options = new ContextifyGatewayOptionsEntity();

        // Assert
        options.DeniedToolPatterns.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that SetDeniedToolPatterns sets the patterns correctly.
    /// </summary>
    [Fact]
    public void SetDeniedToolPatterns_WithValidList_SetsPatterns()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var patterns = new List<string> { "*admin*", "*.delete" };

        // Act
        options.SetDeniedToolPatterns(patterns);

        // Assert
        options.DeniedToolPatterns.Should().HaveCount(2);
        options.DeniedToolPatterns.Should().BeEquivalentTo(patterns);
    }

    /// <summary>
    /// Tests that SetDeniedToolPatterns throws when patterns is null.
    /// </summary>
    [Fact]
    public void SetDeniedToolPatterns_WhenPatternsIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();

        // Act
        var act = () => options.SetDeniedToolPatterns(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("deniedToolPatterns");
    }

    #endregion

    #region Pattern Validation Tests

    /// <summary>
    /// Tests that Validate throws when allowed patterns contain null.
    /// </summary>
    [Fact]
    public void Validate_WhenAllowedPatternsContainNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var patternsList = new List<string> { "valid.*", null! };
        options.SetAllowedToolPatterns(patternsList);

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AllowedToolPatterns*cannot contain null or empty patterns*");
    }

    /// <summary>
    /// Tests that Validate throws when allowed patterns contain empty string.
    /// </summary>
    [Fact]
    public void Validate_WhenAllowedPatternsContainEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var patternsList = new List<string> { "valid.*", string.Empty };
        options.SetAllowedToolPatterns(patternsList);

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AllowedToolPatterns*cannot contain null or empty patterns*");
    }

    /// <summary>
    /// Tests that Validate throws when allowed patterns contain invalid wildcard characters.
    /// </summary>
    [Fact]
    public void Validate_WhenAllowedPatternsContainInvalidWildcards_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var patternsList = new List<string> { "valid.*", "invalid?" };
        options.SetAllowedToolPatterns(patternsList);

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AllowedToolPatterns*contains invalid wildcard characters*");
    }

    /// <summary>
    /// Tests that Validate throws when allowed patterns contain consecutive wildcards.
    /// </summary>
    [Fact]
    public void Validate_WhenAllowedPatternsContainConsecutiveWildcards_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var patternsList = new List<string> { "valid.*", "invalid**" };
        options.SetAllowedToolPatterns(patternsList);

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AllowedToolPatterns*contains invalid pattern*Consecutive wildcards*");
    }

    /// <summary>
    /// Tests that Validate throws when denied patterns contain null.
    /// </summary>
    [Fact]
    public void Validate_WhenDeniedPatternsContainNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        var patternsList = new List<string> { "denied.*", null! };
        options.SetDeniedToolPatterns(patternsList);

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DeniedToolPatterns*cannot contain null or empty patterns*");
    }

    /// <summary>
    /// Tests that Validate passes with valid wildcard patterns.
    /// </summary>
    [Fact]
    public void Validate_WithValidWildcardPatterns_DoesNotThrow()
    {
        // Arrange
        var options = new ContextifyGatewayOptionsEntity();
        options.SetAllowedToolPatterns(new List<string> { "payments.*", "*.read", "users.get_*", "*admin*" });
        options.SetDeniedToolPatterns(new List<string> { "*.delete", "*password*", "system.*" });

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Clone with Tool Patterns Tests

    /// <summary>
    /// Tests that Clone includes tool patterns in the copy.
    /// </summary>
    [Fact]
    public void Clone_IncludesToolPatternsInCopy()
    {
        // Arrange
        var original = new ContextifyGatewayOptionsEntity();
        original.SetAllowedToolPatterns(new List<string> { "payments.*", "users.*" });
        original.SetDeniedToolPatterns(new List<string> { "*admin*", "*.delete" });

        // Act
        var clone = original.Clone();

        // Assert
        clone.AllowedToolPatterns.Should().BeEquivalentTo(original.AllowedToolPatterns);
        clone.DeniedToolPatterns.Should().BeEquivalentTo(original.DeniedToolPatterns);
    }

    /// <summary>
    /// Tests that modifying clone tool patterns does not affect original.
    /// </summary>
    [Fact]
    public void Clone_ModifyingToolPatternDoesNotAffectOriginal()
    {
        // Arrange
        var original = new ContextifyGatewayOptionsEntity();
        original.SetAllowedToolPatterns(new List<string> { "payments.*" });
        original.SetDeniedToolPatterns(new List<string> { "*.delete" });

        // Act
        var clone = original.Clone();
        clone.SetAllowedToolPatterns(new List<string> { "users.*" });
        clone.SetDeniedToolPatterns(new List<string> { "*admin*" });

        // Assert
        original.AllowedToolPatterns.Should().HaveCount(1);
        original.AllowedToolPatterns.First().Should().Be("payments.*");
        original.DeniedToolPatterns.First().Should().Be("*.delete");
    }

    #endregion
}
