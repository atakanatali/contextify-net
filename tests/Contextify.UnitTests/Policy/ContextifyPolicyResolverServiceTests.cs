using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Policy;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Policy;

/// <summary>
/// Unit tests for ContextifyPolicyResolverService.
/// Verifies deterministic policy resolution behavior with various configuration scenarios.
/// Tests cover blacklist/whitelist precedence, deny-by-default behavior, and matching rules.
/// </summary>
public sealed class ContextifyPolicyResolverServiceTests
{
    private readonly ContextifyPolicyResolverService _sut = new();

    #region Blacklist Overrides Whitelist Tests

    /// <summary>
    /// Tests that blacklist overrides whitelist when both contain the same endpoint.
    /// Blacklist should always take precedence regardless of whitelist settings.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenEndpointInBothBlacklistAndWhitelist_DisablesEndpoint()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("GetUser", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "GetUser",
                    HttpMethod = "GET",
                    Enabled = true,
                    TimeoutMs = 5000
                }
            ],
            Blacklist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "GetUser",
                    HttpMethod = "GET"
                }
            ]
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert - Blacklist should override whitelist
        result.IsEnabled.Should().BeFalse("blacklist should override whitelist");
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Blacklist);
        result.TimeoutMs.Should().BeNull("blacklisted endpoints should not apply whitelist settings");
    }

    /// <summary>
    /// Tests that blacklist disables endpoint even when whitelist has it enabled.
    /// Verifies precedence at the route template level.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenEndpointMatchesBothListsByRoute_DisablesEndpoint()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/users/{id}", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = false,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    RouteTemplate = "api/users/{id}",
                    HttpMethod = "GET",
                    Enabled = true,
                    ConcurrencyLimit = 10
                }
            ],
            Blacklist =
            [
                new ContextifyEndpointPolicyDto
                {
                    RouteTemplate = "api/users/{id}",
                    HttpMethod = "GET"
                }
            ]
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeFalse();
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Blacklist);
    }

    #endregion

    #region Deny-by-Default Tests

    /// <summary>
    /// Tests that deny-by-default blocks endpoints not in whitelist.
    /// When DenyByDefault is true and endpoint is not whitelisted, it should be disabled.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenDenyByDefaultAndNotWhitelisted_DisablesEndpoint()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("GetUser", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist = [],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeFalse("deny-by-default should block unmatched endpoints");
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Default);
    }

    /// <summary>
    /// Tests that allow-by-default enables endpoints not in blacklist.
    /// When DenyByDefault is false and endpoint is not blacklisted, it should be enabled.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenAllowByDefaultAndNotBlacklisted_EnablesEndpoint()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("GetUser", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = false,
            Whitelist = [],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeTrue("allow-by-default should enable unmatched endpoints");
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Default);
        result.TimeoutMs.Should().BeNull("default enabled endpoint has no specific timeout");
        result.ConcurrencyLimit.Should().BeNull("default enabled endpoint has no specific concurrency limit");
    }

    /// <summary>
    /// Tests that deny-by-default allows explicitly whitelisted endpoints.
    /// Whitelist should override deny-by-default for matching endpoints.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenDenyByDefaultButWhitelisted_EnablesEndpoint()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("GetUser", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "GetUser",
                    HttpMethod = "GET",
                    Enabled = true,
                    TimeoutMs = 10000,
                    ConcurrencyLimit = 5
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeTrue("whitelist should override deny-by-default");
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Whitelist);
        result.TimeoutMs.Should().Be(10000, "whitelist settings should be applied");
        result.ConcurrencyLimit.Should().Be(5, "whitelist settings should be applied");
    }

    #endregion

    #region Whitelist Enablement Tests

    /// <summary>
    /// Tests that whitelist enables endpoint and applies configured settings.
    /// Whitelist should enable endpoint and apply timeout, concurrency, and rate limit settings.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenWhitelistedWithFullSettings_AppliesAllSettings()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("ProcessData", "POST");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "ProcessData",
                    HttpMethod = "POST",
                    Enabled = true,
                    TimeoutMs = 30000,
                    ConcurrencyLimit = 20,
                    AuthPropagationMode = ContextifyAuthPropagationMode.BearerToken,
                    RateLimitPolicy = ContextifyRateLimitPolicyDto.FixedWindow(
                        permitLimit: 100,
                        windowMs: 60000,
                        queueLimit: 10)
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeTrue();
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Whitelist);
        result.TimeoutMs.Should().Be(30000);
        result.ConcurrencyLimit.Should().Be(20);
        result.AuthPropagationMode.Should().Be(ContextifyAuthPropagationMode.BearerToken);
        result.RateLimitPermitLimit.Should().Be(100);
        result.RateLimitWindowMs.Should().Be(60000);
        result.RateLimitQueueLimit.Should().Be(10);
    }

    /// <summary>
    /// Tests that whitelist with Enabled=false disables the endpoint.
    /// Whitelist entry can explicitly disable an endpoint even though it's whitelisted.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenWhitelistedButDisabled_DisablesEndpoint()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("DeprecatedEndpoint", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "DeprecatedEndpoint",
                    HttpMethod = "GET",
                    Enabled = false,
                    TimeoutMs = 5000
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeFalse("whitelist entry with Enabled=false should disable endpoint");
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Whitelist);
    }

    #endregion

    #region Matching Precedence Tests

    /// <summary>
    /// Tests that operation ID has highest matching priority.
    /// Operation ID match should be preferred over route and display name.
    /// </summary>
    [Fact]
    public void ResolvePriority_WhenOperationIdProvided_MatchesByOperationId()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("CreateOrder", "POST");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                // Route-based match (lower priority)
                new ContextifyEndpointPolicyDto
                {
                    RouteTemplate = "api/orders",
                    HttpMethod = "POST",
                    Enabled = false
                },
                // Operation ID match (highest priority)
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "CreateOrder",
                    HttpMethod = "POST",
                    Enabled = true,
                    TimeoutMs = 15000
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeTrue("operation ID should take priority over route");
        result.TimeoutMs.Should().Be(15000, "operation ID policy settings should be applied");
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Whitelist);
    }

    /// <summary>
    /// Tests that route template + HTTP method has second priority.
    /// Route match should be used when operation ID is not available.
    /// </summary>
    [Fact]
    public void ResolvePriority_WhenOnlyRouteProvided_MatchesByRouteAndMethod()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/products/{id}", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                // Display name match (lower priority)
                new ContextifyEndpointPolicyDto
                {
                    DisplayName = "Get Product",
                    HttpMethod = "GET",
                    Enabled = false
                },
                // Route match (higher priority)
                new ContextifyEndpointPolicyDto
                {
                    RouteTemplate = "api/products/{id}",
                    HttpMethod = "GET",
                    Enabled = true,
                    TimeoutMs = 8000
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeTrue("route should take priority over display name");
        result.TimeoutMs.Should().Be(8000);
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Whitelist);
    }

    /// <summary>
    /// Tests that display name is used as fallback when operation ID and route are not available.
    /// </summary>
    [Fact]
    public void ResolvePriority_WhenOnlyDisplayNameProvided_MatchesByDisplayName()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromDisplayName("List All Users", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    DisplayName = "List All Users",
                    HttpMethod = "GET",
                    Enabled = true,
                    ConcurrencyLimit = 50
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeTrue();
        result.ConcurrencyLimit.Should().Be(50);
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Whitelist);
    }

    /// <summary>
    /// Tests that HTTP method is considered in matching.
    /// Same route with different methods should match independently.
    /// </summary>
    [Fact]
    public void ResolveMatching_WhenSameRouteDifferentMethods_MatchesCorrectly()
    {
        // Arrange
        var getDescriptor = ContextifyEndpointDescriptor.FromRoute("api/users", "GET");
        var postDescriptor = ContextifyEndpointDescriptor.FromRoute("api/users", "POST");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    RouteTemplate = "api/users",
                    HttpMethod = "GET",
                    Enabled = true,
                    TimeoutMs = 5000
                },
                new ContextifyEndpointPolicyDto
                {
                    RouteTemplate = "api/users",
                    HttpMethod = "POST",
                    Enabled = false
                }
            ],
            Blacklist = []
        };

        // Act
        var getResult = _sut.ResolvePolicy(getDescriptor, policy);
        var postResult = _sut.ResolvePolicy(postDescriptor, policy);

        // Assert
        getResult.IsEnabled.Should().BeTrue("GET should be enabled");
        getResult.TimeoutMs.Should().Be(5000);
        postResult.IsEnabled.Should().BeFalse("POST should be disabled");
    }

    /// <summary>
    /// Tests that policy matches when HTTP method is not specified in the policy.
    /// Policy with null HTTP method should match any HTTP method.
    /// </summary>
    [Fact]
    public void ResolveMatching_WhenPolicyHasNullMethod_MatchesAnyMethod()
    {
        // Arrange
        var getDescriptor = ContextifyEndpointDescriptor.FromRoute("api/health", "GET");
        var postDescriptor = ContextifyEndpointDescriptor.FromRoute("api/health", "POST");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    RouteTemplate = "api/health",
                    HttpMethod = null, // Should match any method
                    Enabled = true
                }
            ],
            Blacklist = []
        };

        // Act
        var getResult = _sut.ResolvePolicy(getDescriptor, policy);
        var postResult = _sut.ResolvePolicy(postDescriptor, policy);

        // Assert
        getResult.IsEnabled.Should().BeTrue();
        postResult.IsEnabled.Should().BeTrue();
    }

    #endregion

    #region Null-Safe Behavior Tests

    /// <summary>
    /// Tests that null endpoint descriptor throws ArgumentNullException.
    /// Service should validate input parameters and fail fast.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenDescriptorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var policy = new ContextifyPolicyConfigDto();

        // Act
        var act = () => _sut.ResolvePolicy(null!, policy);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("endpointDescriptor");
    }

    /// <summary>
    /// Tests that null policy config throws ArgumentNullException.
    /// Service should validate input parameters and fail fast.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenPolicyConfigIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("Test", "GET");

        // Act
        var act = () => _sut.ResolvePolicy(descriptor, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("policyConfig");
    }

    /// <summary>
    /// Tests that descriptor with no identifying properties throws ArgumentException.
    /// At least one of operation ID, route template, or display name must be set.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenDescriptorHasNoIdentifyingProperties_ThrowsArgumentException()
    {
        // Arrange
        var descriptor = new ContextifyEndpointDescriptor();
        var policy = new ContextifyPolicyConfigDto();

        // Act
        var act = () => _sut.ResolvePolicy(descriptor, policy);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*OperationId*RouteTemplate*DisplayName*");
    }

    /// <summary>
    /// Tests that null collections in policy config are handled gracefully.
    /// Empty lists should be treated the same as null for safety.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenCollectionsAreNull_HandlesGracefully()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("Test", "GET");
        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = false,
            Whitelist = null!,
            Blacklist = null!
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert - Should treat null collections as empty
        result.IsEnabled.Should().BeTrue("allow-by-default should apply when collections are null");
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Default);
    }

    /// <summary>
    /// Tests that empty whitelist and blacklist are handled correctly.
    /// Service should not throw on empty collections.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenCollectionsAreEmpty_HandlesCorrectly()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("Test", "GET");
        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist = [],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeFalse("deny-by-default should apply with empty collections");
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Default);
    }

    /// <summary>
    /// Tests that endpoint with missing optional fields still matches correctly.
    /// Only the provided fields should be used for matching.
    /// </summary>
    [Fact]
    public void ResolveMatching_WhenDescriptorHasPartialFields_MatchesCorrectly()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("GetSettings", null);

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "GetSettings",
                    HttpMethod = null, // Should match regardless of method
                    Enabled = true
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeTrue("should match without HTTP method");
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Whitelist);
    }

    #endregion

    #region Policy Settings Application Tests

    /// <summary>
    /// Tests that auth propagation mode is correctly applied from policy.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenAuthModeSpecified_AppliesCorrectMode()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("SecureEndpoint", "POST");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "SecureEndpoint",
                    HttpMethod = "POST",
                    Enabled = true,
                    AuthPropagationMode = ContextifyAuthPropagationMode.Cookies
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.AuthPropagationMode.Should().Be(ContextifyAuthPropagationMode.Cookies);
    }

    /// <summary>
    /// Tests that default auth propagation mode is Infer when not specified.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenAuthModeNotSpecified_UsesDefault()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("DefaultAuth", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = false,
            Whitelist = [],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.AuthPropagationMode.Should().Be(ContextifyAuthPropagationMode.Infer);
    }

    /// <summary>
    /// Tests that rate limit settings are only applied when configured.
    /// Null rate limit policy should result in null rate limit properties.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenRateLimitNotConfigured_ReturnsNullRateLimitProperties()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("NoRateLimit", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "NoRateLimit",
                    HttpMethod = "GET",
                    Enabled = true,
                    RateLimitPolicy = null
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.RateLimitPermitLimit.Should().BeNull();
        result.RateLimitWindowMs.Should().BeNull();
        result.RateLimitQueueLimit.Should().BeNull();
    }

    /// <summary>
    /// Tests that all nullable policy settings can be independently null.
    /// Verifies partial configuration scenarios.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenPartialSettings_AppliesOnlyNonNullSettings()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("PartialConfig", "PUT");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "PartialConfig",
                    HttpMethod = "PUT",
                    Enabled = true,
                    TimeoutMs = null,
                    ConcurrencyLimit = 100,
                    AuthPropagationMode = ContextifyAuthPropagationMode.None,
                    RateLimitPolicy = null
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeTrue();
        result.TimeoutMs.Should().BeNull();
        result.ConcurrencyLimit.Should().Be(100);
        result.AuthPropagationMode.Should().Be(ContextifyAuthPropagationMode.None);
        result.RateLimitPermitLimit.Should().BeNull();
        result.RateLimitWindowMs.Should().BeNull();
        result.RateLimitQueueLimit.Should().BeNull();
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Tests that whitelist containing the same endpoint multiple times uses first match.
    /// First match in collection order should be applied.
    /// </summary>
    [Fact]
    public void ResolvePolicy_WhenDuplicateEntriesInWhitelist_UsesFirstMatch()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("Duplicate", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "Duplicate",
                    HttpMethod = "GET",
                    Enabled = true,
                    TimeoutMs = 5000
                },
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "Duplicate",
                    HttpMethod = "GET",
                    Enabled = true,
                    TimeoutMs = 10000 // Different value
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.TimeoutMs.Should().Be(5000, "first match should be used");
    }

    /// <summary>
    /// Tests case-sensitive matching for operation ID.
    /// Different cases should not match.
    /// </summary>
    [Fact]
    public void ResolveMatching_WhenOperationIdCaseDifferent_DoesNotMatch()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromOperationId("getuser", "GET");

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    OperationId = "GetUser", // Different case
                    HttpMethod = "GET",
                    Enabled = true
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeFalse("case should not match");
        result.ResolutionSource.Should().Be(ContextifyPolicyResolutionSource.Default);
    }

    /// <summary>
    /// Tests case-insensitive matching for HTTP method.
    /// Different cases should still match.
    /// </summary>
    [Fact]
    public void ResolveMatching_WhenHttpMethodCaseDifferent_MatchesCorrectly()
    {
        // Arrange
        var descriptor = ContextifyEndpointDescriptor.FromRoute("api/test", "get"); // lowercase

        var policy = new ContextifyPolicyConfigDto
        {
            DenyByDefault = true,
            Whitelist =
            [
                new ContextifyEndpointPolicyDto
                {
                    RouteTemplate = "api/test",
                    HttpMethod = "GET", // uppercase
                    Enabled = true
                }
            ],
            Blacklist = []
        };

        // Act
        var result = _sut.ResolvePolicy(descriptor, policy);

        // Assert
        result.IsEnabled.Should().BeTrue("HTTP method should be case-insensitive");
    }

    #endregion
}
