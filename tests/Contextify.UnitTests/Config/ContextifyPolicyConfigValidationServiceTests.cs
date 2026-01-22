using System;
using System.Collections.Generic;
using System.Linq;
using Contextify.Config.Abstractions.Policy;
using Contextify.Config.Abstractions.Validation;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Config;

/// <summary>
/// Unit tests for ContextifyPolicyConfigValidationService.
/// Verifies schema version validation, field validation, and cross-field rule checking.
/// Tests ensure backward compatibility and proper error/warning reporting.
/// </summary>
public sealed class ContextifyPolicyConfigValidationServiceTests
{
    private readonly ContextifyPolicyConfigValidationService _validationService;

    /// <summary>
    /// Initializes test fixtures with a new validation service instance.
    /// </summary>
    public ContextifyPolicyConfigValidationServiceTests()
    {
        _validationService = new ContextifyPolicyConfigValidationService();
    }

    #region SchemaVersion Tests

    /// <summary>
    /// Tests that missing SchemaVersion (0) is treated as version 1 with a warning.
    /// Verifies backward compatibility for configurations without schema version.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenSchemaVersionIsZero_ReturnsWarningAndTreatsAsVersion1()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 0, // Missing schema version
            DenyByDefault = false,
            Whitelist = [],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeTrue("validation should pass with backward compatibility");
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("SchemaVersion is missing or 0", "should warn about missing version");
        result.Errors.Should().BeEmpty("should not produce errors for backward compatibility");
    }

    /// <summary>
    /// Tests that SchemaVersion 1 is accepted without warnings or errors.
    /// Verifies the current supported schema version works correctly.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenSchemaVersionIs1_ReturnsSuccess()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            DenyByDefault = false,
            Whitelist = [],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeTrue("schema version 1 should be valid");
        result.HasMessages.Should().BeFalse("should not produce warnings or errors");
    }

    /// <summary>
    /// Tests that negative SchemaVersion produces an error.
    /// Verifies validation rejects invalid schema versions.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenSchemaVersionIsNegative_ReturnsError()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = -1,
            DenyByDefault = false,
            Whitelist = [],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeFalse("negative schema version should be invalid");
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("SchemaVersion must be at least 1");
    }

    /// <summary>
    /// Tests that unsupported SchemaVersion produces an error.
    /// Verifies validation rejects future schema versions.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenSchemaVersionIsUnsupported_ReturnsError()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 999,
            DenyByDefault = false,
            Whitelist = [],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeFalse("unsupported schema version should be invalid");
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("not supported")
            .And.Contain("Maximum supported version is 1");
    }

    #endregion

    #region Field Validation Tests

    /// <summary>
    /// Tests that null configuration produces an error.
    /// Verifies null input is properly rejected.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenConfigIsNull_ReturnsError()
    {
        // Arrange
        ContextifyPolicyConfigDto? config = null;

        // Act
        var result = _validationService.ValidatePolicyConfig(config!);

        // Assert
        result.IsValid.Should().BeFalse("null config should be invalid");
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("cannot be null");
    }

    /// <summary>
    /// Tests that policies without identifying information produce errors.
    /// Verifies at least one of OperationId, RouteTemplate, or DisplayName is required.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenPolicyHasNoIdentifyingInfo_ReturnsError()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            DenyByDefault = false,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = null,
                    RouteTemplate = null,
                    DisplayName = null
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeFalse("policy without identifying info should be invalid");
        result.Errors.Should().Contain(e => e.Contains("no identifying information"));
    }

    /// <summary>
    /// Tests that policies with RouteTemplate but no HttpMethod produce a warning.
    /// Verifies warning when HTTP method is missing with route template.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenRouteTemplateWithoutHttpMethod_ReturnsWarning()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            DenyByDefault = false,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "test/op",
                    RouteTemplate = "/api/test",
                    HttpMethod = null
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeTrue("validation should pass but produce a warning");
        result.Warnings.Should().Contain(w => w.Contains("RouteTemplate but no HttpMethod"));
    }

    /// <summary>
    /// Tests that null policies in the list produce errors.
    /// Verifies null policy entries are properly rejected.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenPolicyListContainsNull_ReturnsError()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            DenyByDefault = false,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto { OperationId = "valid" },
                null!
            ],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeFalse("null policy should be invalid");
        result.Errors.Should().Contain(e => e.Contains("is null"));
    }

    #endregion

    #region Cross-Field Validation Tests

    /// <summary>
    /// Tests that conflicting policies (same operation ID in both lists) produce a warning.
    /// Verifies blacklist takes precedence warning is issued.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenOperationIdInBothLists_ReturnsWarning()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            DenyByDefault = false,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto { OperationId = "conflict/op" }
            ],
            Blacklist =
            [
                new ContextifyEndpointPolicyDto { OperationId = "conflict/op" }
            ]
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeTrue("conflicting policies should be valid but produce a warning");
        result.Warnings.Should().Contain(w => w.Contains("conflict/op") && w.Contains("Blacklist takes precedence"));
    }

    /// <summary>
    /// Tests that allow-by-default mode produces a security warning.
    /// Verifies warning about less secure default setting.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenDenyByDefaultIsFalse_ReturnsSecurityWarning()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            DenyByDefault = false,
            Whitelist = [],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeTrue("allow-by-default should be valid but produce a warning");
        result.Warnings.Should().Contain(w => w.Contains("DenyByDefault is false") && w.Contains("less secure"));
    }

    /// <summary>
    /// Tests that deny-by-default with empty whitelist produces an error.
    /// Verifies no endpoints would be accessible in this configuration.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenDenyByDefaultTrueAndEmptyWhitelist_ReturnsError()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            DenyByDefault = true,
            Whitelist = [],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeFalse("deny-by-default with empty whitelist should be invalid");
        result.Errors.Should().Contain(e => e.Contains("DenyByDefault is true but Whitelist is empty"));
    }

    #endregion

    #region Rate Limit Validation Tests

    /// <summary>
    /// Tests that rate limit with PermitLimit less than 1 produces an error.
    /// Verifies minimum value validation for rate limiting.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenRateLimitPermitLimitLessThan1_ReturnsError()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            DenyByDefault = false,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "test/op",
                    RateLimitPolicy = new ContextifyRateLimitPolicyDto
                    {
                        Strategy = "FixedWindow",
                        PermitLimit = 0,
                        WindowMs = 60000
                    }
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeFalse("rate limit with 0 permit limit should be invalid");
        result.Errors.Should().Contain(e => e.Contains("RateLimitPolicy.PermitLimit") && e.Contains("Must be at least 1"));
    }

    /// <summary>
    /// Tests that rate limit with non-positive WindowMs produces an error.
    /// Verifies time window must be positive.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenRateLimitWindowMsIsZero_ReturnsError()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            DenyByDefault = false,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "test/op",
                    RateLimitPolicy = new ContextifyRateLimitPolicyDto
                    {
                        Strategy = "FixedWindow",
                        PermitLimit = 10,
                        WindowMs = 0
                    }
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeFalse("rate limit with zero window ms should be invalid");
        result.Errors.Should().Contain(e => e.Contains("RateLimitPolicy.WindowMs") && e.Contains("greater than zero"));
    }

    /// <summary>
    /// Tests that very short rate limit window produces a warning.
    /// Verifies warning for potentially ineffective rate limiting.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenRateLimitWindowMsIsVeryShort_ReturnsWarning()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            DenyByDefault = false,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "test/op",
                    RateLimitPolicy = new ContextifyRateLimitPolicyDto
                    {
                        Strategy = "FixedWindow",
                        PermitLimit = 10,
                        WindowMs = 100
                    }
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeTrue("validation should pass but produce a warning");
        result.Warnings.Should().Contain(w => w.Contains("very short RateLimitPolicy.WindowMs"));
    }

    #endregion

    #region Valid Configuration Tests

    /// <summary>
    /// Tests that a valid configuration passes validation.
    /// Verifies complete valid configuration is accepted.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenConfigIsValid_ReturnsSuccess()
    {
        // Arrange
        var config = new ContextifyPolicyConfigDto
        {
            SchemaVersion = 1,
            DenyByDefault = false,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "api/read",
                    DisplayName = "Read API",
                    HttpMethod = "GET",
                    RouteTemplate = "/api/read",
                    RateLimitPolicy = new ContextifyRateLimitPolicyDto
                    {
                        Strategy = "FixedWindow",
                        PermitLimit = 100,
                        WindowMs = 60000
                    }
                }
            ],
            Blacklist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "api/delete",
                    DisplayName = "Delete API"
                }
            ]
        };

        // Act
        var result = _validationService.ValidatePolicyConfig(config);

        // Assert
        result.IsValid.Should().BeTrue("valid configuration should pass validation");
        result.HasMessages.Should().BeFalse("valid configuration should not produce warnings or errors");
    }

    /// <summary>
    /// Tests that default factory configurations pass validation.
    /// Verifies AllowByDefault and SecureByDefault factory methods work.
    /// </summary>
    [Fact]
    public void ValidatePolicyConfig_WhenUsingFactoryMethods_ReturnsSuccessWithWarnings()
    {
        // Arrange
        var allowByDefault = ContextifyPolicyConfigDto.AllowByDefault();
        var secureByDefault = ContextifyPolicyConfigDto.SecureByDefault()
            with { Whitelist = [new ContextifyEndpointPolicyDto { OperationId = "test" }] };

        // Act
        var allowResult = _validationService.ValidatePolicyConfig(allowByDefault);
        var secureResult = _validationService.ValidatePolicyConfig(secureByDefault);

        // Assert
        allowResult.IsValid.Should().BeTrue();
        allowResult.Warnings.Should().Contain(w => w.Contains("DenyByDefault is false"));

        secureResult.IsValid.Should().BeTrue();
        secureResult.HasMessages.Should().BeFalse();
    }

    #endregion
}
