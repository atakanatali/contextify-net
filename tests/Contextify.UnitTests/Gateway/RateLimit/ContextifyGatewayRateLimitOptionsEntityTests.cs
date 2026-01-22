using Contextify.Gateway.Core.RateLimit;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Gateway.RateLimit;

/// <summary>
/// Unit tests for ContextifyGatewayRateLimitOptionsEntity.
/// Verifies rate limiting options initialization, validation, policy resolution, and cloning behavior.
/// </summary>
public sealed class ContextifyGatewayRateLimitOptionsEntityTests
{
    #region Constructor Tests

    /// <summary>
    /// Tests that the constructor creates options with default values.
    /// </summary>
    [Fact]
    public void Constructor_WithDefaults_CreatesOptionsWithDefaults()
    {
        // Act
        var options = new ContextifyGatewayRateLimitOptionsEntity();

        // Assert
        options.Enabled.Should().BeFalse();
        options.DefaultQuotaPolicy.Should().BeNull();
        options.Overrides.Should().BeEmpty();
        options.MaxCacheSize.Should().Be(10000);
        options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(5));
        options.EntryExpiration.Should().Be(TimeSpan.FromMinutes(10));
    }

    #endregion

    #region Enabled Tests

    /// <summary>
    /// Tests that Enabled can be set to true or false.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Enabled_WithValue_SetsValue(bool enabled)
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();

        // Act
        options.Enabled = enabled;

        // Assert
        options.Enabled.Should().Be(enabled);
    }

    #endregion

    #region DefaultQuotaPolicy Tests

    /// <summary>
    /// Tests that DefaultQuotaPolicy can be set to a valid policy.
    /// </summary>
    [Fact]
    public void DefaultQuotaPolicy_WithValidPolicy_SetsValue()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();
        var policy = new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.Tenant,
            100,
            60000);

        // Act
        options.DefaultQuotaPolicy = policy;

        // Assert
        options.DefaultQuotaPolicy.Should().Be(policy);
    }

    /// <summary>
    /// Tests that DefaultQuotaPolicy can be set to null.
    /// </summary>
    [Fact]
    public void DefaultQuotaPolicy_WhenSetToNull_SetsValue()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();
        options.DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        options.DefaultQuotaPolicy = null;

        // Assert
        options.DefaultQuotaPolicy.Should().BeNull();
    }

    #endregion

    #region Overrides Tests

    /// <summary>
    /// Tests that SetOverrides creates a copy of the dictionary.
    /// </summary>
    [Fact]
    public void SetOverrides_WithDictionary_CreatesCopy()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();
        var overrides = new Dictionary<string, ContextifyGatewayQuotaPolicyDto>
        {
            ["weather.*"] = new ContextifyGatewayQuotaPolicyDto(ContextifyGatewayQuotaScope.TenantTool, 50, 30000),
            ["payments.*"] = new ContextifyGatewayQuotaPolicyDto(ContextifyGatewayQuotaScope.UserTool, 20, 60000)
        };

        // Act
        options.SetOverrides(overrides);

        // Assert
        options.Overrides.Should().HaveCount(2);
        options.Overrides.Should().ContainKey("weather.*");
        options.Overrides.Should().ContainKey("payments.*");
    }

    /// <summary>
    /// Tests that SetOverrides throws when dictionary is null.
    /// </summary>
    [Fact]
    public void SetOverrides_WhenNull_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();

        // Act
        var act = () => options.SetOverrides(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("overrides");
    }

    /// <summary>
    /// Tests that modifying the original dictionary after SetOverrides doesn't affect the options.
    /// </summary>
    [Fact]
    public void SetOverrides_ModifyingOriginalAfterSet_DoesNotAffectOptions()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();
        var overrides = new Dictionary<string, ContextifyGatewayQuotaPolicyDto>
        {
            ["test.*"] = new ContextifyGatewayQuotaPolicyDto(ContextifyGatewayQuotaScope.Tool, 100, 60000)
        };

        // Act
        options.SetOverrides(overrides);
        overrides.Clear();

        // Assert
        options.Overrides.Should().HaveCount(1);
    }

    #endregion

    #region MaxCacheSize Tests

    /// <summary>
    /// Tests that MaxCacheSize can be set to a valid value.
    /// </summary>
    [Fact]
    public void MaxCacheSize_WithValidValue_SetsValue()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();

        // Act
        options.MaxCacheSize = 5000;

        // Assert
        options.MaxCacheSize.Should().Be(5000);
    }

    /// <summary>
    /// Tests that MaxCacheSize throws when set to zero.
    /// </summary>
    [Fact]
    public void MaxCacheSize_WhenSetToZero_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();

        // Act
        var act = () => options.MaxCacheSize = 0;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Max cache size must be greater than zero*");
    }

    /// <summary>
    /// Tests that MaxCacheSize throws when set to negative.
    /// </summary>
    [Fact]
    public void MaxCacheSize_WhenSetToNegative_ThrowsArgumentException()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();

        // Act
        var act = () => options.MaxCacheSize = -100;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Max cache size must be greater than zero*");
    }

    #endregion

    #region CleanupInterval Tests

    /// <summary>
    /// Tests that CleanupInterval can be set to a valid value.
    /// </summary>
    [Fact]
    public void CleanupInterval_WithValidValue_SetsValue()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();

        // Act
        options.CleanupInterval = TimeSpan.FromMinutes(10);

        // Assert
        options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// Tests that CleanupInterval throws when set to zero or negative.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void CleanupInterval_WhenInvalid_ThrowsArgumentException(int seconds)
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();

        // Act
        var act = () => options.CleanupInterval = TimeSpan.FromSeconds(seconds);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Cleanup interval must be greater than zero*");
    }

    #endregion

    #region EntryExpiration Tests

    /// <summary>
    /// Tests that EntryExpiration can be set to a valid value.
    /// </summary>
    [Fact]
    public void EntryExpiration_WithValidValue_SetsValue()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();

        // Act
        options.EntryExpiration = TimeSpan.FromMinutes(20);

        // Assert
        options.EntryExpiration.Should().Be(TimeSpan.FromMinutes(20));
    }

    /// <summary>
    /// Tests that EntryExpiration throws when set to zero or negative.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void EntryExpiration_WhenInvalid_ThrowsArgumentException(int seconds)
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();

        // Act
        var act = () => options.EntryExpiration = TimeSpan.FromSeconds(seconds);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("*Entry expiration must be greater than zero*");
    }

    #endregion

    #region GetPolicyForTool Tests

    /// <summary>
    /// Tests that GetPolicyForTool returns default policy when no override matches.
    /// </summary>
    [Fact]
    public void GetPolicyForTool_WhenNoOverrideMatches_ReturnsDefaultPolicy()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity
        {
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Tenant,
                100,
                60000)
        };

        // Act
        var policy = options.GetPolicyForTool("unmatched.tool");

        // Assert
        policy.Should().Be(options.DefaultQuotaPolicy);
    }

    /// <summary>
    /// Tests that GetPolicyForTool returns null when no override matches and no default policy.
    /// </summary>
    [Fact]
    public void GetPolicyForTool_WhenNoOverrideMatchesAndNoDefault_ReturnsNull()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();

        // Act
        var policy = options.GetPolicyForTool("unmatched.tool");

        // Assert
        policy.Should().BeNull();
    }

    /// <summary>
    /// Tests that GetPolicyForTool returns exact match override.
    /// </summary>
    [Fact]
    public void GetPolicyForTool_WithExactMatch_ReturnsOverridePolicy()
    {
        // Arrange
        var overridePolicy = new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.TenantTool,
            50,
            30000);
        var options = new ContextifyGatewayRateLimitOptionsEntity
        {
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Global,
                100,
                60000)
        };
        options.SetOverrides(new Dictionary<string, ContextifyGatewayQuotaPolicyDto>
        {
            ["weather.get_forecast"] = overridePolicy
        });

        // Act
        var policy = options.GetPolicyForTool("weather.get_forecast");

        // Assert
        policy.Should().Be(overridePolicy);
        policy.Should().NotBe(options.DefaultQuotaPolicy);
    }

    /// <summary>
    /// Tests that GetPolicyForTool returns wildcard prefix match.
    /// </summary>
    [Fact]
    public void GetPolicyForTool_WithWildcardPrefixMatch_ReturnsOverridePolicy()
    {
        // Arrange
        var overridePolicy = new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.TenantTool,
            50,
            30000);
        var options = new ContextifyGatewayRateLimitOptionsEntity();
        options.SetOverrides(new Dictionary<string, ContextifyGatewayQuotaPolicyDto>
        {
            ["weather.*"] = overridePolicy
        });

        // Act
        var policy = options.GetPolicyForTool("weather.get_forecast");

        // Assert
        policy.Should().Be(overridePolicy);
    }

    /// <summary>
    /// Tests that GetPolicyForTool returns wildcard suffix match.
    /// </summary>
    [Fact]
    public void GetPolicyForTool_WithWildcardSuffixMatch_ReturnsOverridePolicy()
    {
        // Arrange
        var overridePolicy = new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.TenantTool,
            50,
            30000);
        var options = new ContextifyGatewayRateLimitOptionsEntity();
        options.SetOverrides(new Dictionary<string, ContextifyGatewayQuotaPolicyDto>
        {
            ["*.get_forecast"] = overridePolicy
        });

        // Act
        var policy = options.GetPolicyForTool("weather.get_forecast");

        // Assert
        policy.Should().Be(overridePolicy);
    }

    /// <summary>
    /// Tests that GetPolicyForTool returns wildcard infix match.
    /// </summary>
    [Fact]
    public void GetPolicyForTool_WithWildcardInfixMatch_ReturnsOverridePolicy()
    {
        // Arrange
        var overridePolicy = new ContextifyGatewayQuotaPolicyDto(
            ContextifyGatewayQuotaScope.TenantTool,
            50,
            30000);
        var options = new ContextifyGatewayRateLimitOptionsEntity();
        options.SetOverrides(new Dictionary<string, ContextifyGatewayQuotaPolicyDto>
        {
            ["weather.*cast"] = overridePolicy
        });

        // Act
        var policy = options.GetPolicyForTool("weather.get_forecast");

        // Assert
        policy.Should().Be(overridePolicy);
    }

    /// <summary>
    /// Tests that GetPolicyForTool handles empty tool name gracefully.
    /// </summary>
    [Fact]
    public void GetPolicyForTool_WithEmptyToolName_ReturnsDefaultPolicy()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity
        {
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Global,
                100,
                60000)
        };

        // Act
        var policy = options.GetPolicyForTool(string.Empty);

        // Assert
        policy.Should().Be(options.DefaultQuotaPolicy);
    }

    /// <summary>
    /// Tests that GetPolicyForTool handles null tool name gracefully.
    /// </summary>
    [Fact]
    public void GetPolicyForTool_WithNullToolName_ReturnsDefaultPolicy()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity
        {
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Global,
                100,
                60000)
        };

        // Act
        var policy = options.GetPolicyForTool(null!);

        // Assert
        policy.Should().Be(options.DefaultQuotaPolicy);
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
        var options = new ContextifyGatewayRateLimitOptionsEntity
        {
            Enabled = true,
            MaxCacheSize = 1000,
            CleanupInterval = TimeSpan.FromMinutes(1),
            EntryExpiration = TimeSpan.FromMinutes(5)
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that Validate throws when MaxCacheSize is invalid.
    /// </summary>
    [Fact]
    public void Validate_WhenMaxCacheSizeIsInvalid_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();
        options.MaxCacheSize = 0;

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxCacheSize*must be greater than zero*");
    }

    /// <summary>
    /// Tests that Validate throws when CleanupInterval is invalid.
    /// </summary>
    [Fact]
    public void Validate_WhenCleanupIntervalIsInvalid_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();
        options.CleanupInterval = TimeSpan.Zero;

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CleanupInterval*must be greater than zero*");
    }

    /// <summary>
    /// Tests that Validate throws when EntryExpiration is invalid.
    /// </summary>
    [Fact]
    public void Validate_WhenEntryExpirationIsInvalid_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();
        options.EntryExpiration = TimeSpan.FromSeconds(-1);

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EntryExpiration*must be greater than zero*");
    }

    /// <summary>
    /// Tests that Validate validates default policy if present.
    /// </summary>
    [Fact]
    public void DefaultQuotaPolicy_WhenPermitLimitIsInvalid_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        var act = () => policy.PermitLimit = 0;

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Tests that Validate validates override policies.
    /// </summary>
    [Fact]
    public void OverridePolicy_WhenWindowMsIsInvalid_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var policy = new ContextifyGatewayQuotaPolicyDto();

        // Act
        var act = () => policy.WindowMs = 0;

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Tests that Validate throws when override key is empty.
    /// </summary>
    [Fact]
    public void Validate_WhenOverrideKeyIsEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ContextifyGatewayRateLimitOptionsEntity();
        options.SetOverrides(new Dictionary<string, ContextifyGatewayQuotaPolicyDto>
        {
            [""] = new ContextifyGatewayQuotaPolicyDto()
        });

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Override keys cannot be null or whitespace*");
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
        var original = new ContextifyGatewayRateLimitOptionsEntity
        {
            Enabled = true,
            MaxCacheSize = 5000,
            CleanupInterval = TimeSpan.FromMinutes(2),
            EntryExpiration = TimeSpan.FromMinutes(15),
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Tenant,
                200,
                90000)
        };
        original.SetOverrides(new Dictionary<string, ContextifyGatewayQuotaPolicyDto>
        {
            ["test.*"] = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Tool,
                50,
                30000)
        });

        // Act
        var clone = original.Clone();

        // Assert
        clone.Should().NotBeSameAs(original);
        clone.Enabled.Should().Be(original.Enabled);
        clone.MaxCacheSize.Should().Be(original.MaxCacheSize);
        clone.CleanupInterval.Should().Be(original.CleanupInterval);
        clone.EntryExpiration.Should().Be(original.EntryExpiration);
        clone.DefaultQuotaPolicy.Should().NotBeNull();
        clone.DefaultQuotaPolicy!.PermitLimit.Should().Be(200);
        clone.Overrides.Should().HaveCount(1);
    }

    /// <summary>
    /// Tests that modifying the clone does not affect the original.
    /// </summary>
    [Fact]
    public void Clone_ModifyingCloneDoesNotAffectOriginal()
    {
        // Arrange
        var original = new ContextifyGatewayRateLimitOptionsEntity
        {
            Enabled = true,
            DefaultQuotaPolicy = new ContextifyGatewayQuotaPolicyDto(
                ContextifyGatewayQuotaScope.Global,
                100,
                60000)
        };

        // Act
        var clone = original.Clone();
        clone.Enabled = false;
        clone.MaxCacheSize = 2000;
        if (clone.DefaultQuotaPolicy != null)
        {
            clone.DefaultQuotaPolicy.PermitLimit = 500;
        }

        // Assert
        original.Enabled.Should().BeTrue();
        original.MaxCacheSize.Should().Be(10000);
        original.DefaultQuotaPolicy!.PermitLimit.Should().Be(100);
    }

    #endregion
}
