using Contextify.OpenApi.Enrichment;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Moq;
using Xunit;

namespace Contextify.OpenApi.UnitTests.Enrichment;

/// <summary>
/// Unit tests for OpenApiOperationMatcher behavior.
/// Verifies endpoint-to-operation matching strategies.
/// </summary>
public sealed class OpenApiOperationMatcherTests
{
    /// <summary>
    /// Tests that MatchOperation matches by operation ID when available.
    /// Operation ID matching has highest priority.
    /// </summary>
    [Fact]
    public void MatchOperation_WhenOperationIdProvided_MatchesById()
    {
        // Arrange
        var document = CreateTestDocument();
        var logger = new Mock<ILogger<OpenApiOperationMatcher>>().Object;
        var matcher = new OpenApiOperationMatcher(document, logger);

        // Act
        var result = matcher.MatchOperation(
            routeTemplate: "/api/tools",
            httpMethod: "POST",
            operationId: "CreateTool",
            displayName: null);

        // Assert
        result.Should().NotBeNull("operation ID should match");
        result!.OperationId.Should().Be("CreateTool");
    }

    /// <summary>
    /// Tests that MatchOperation matches by route and method when operation ID is not available.
    /// Route + method matching has medium priority.
    /// </summary>
    [Fact]
    public void MatchOperation_WhenNoOperationId_MatchesByRouteAndMethod()
    {
        // Arrange
        var document = CreateTestDocument();
        var logger = new Mock<ILogger<OpenApiOperationMatcher>>().Object;
        var matcher = new OpenApiOperationMatcher(document, logger);

        // Act
        var result = matcher.MatchOperation(
            routeTemplate: "/api/tools/{id}",
            httpMethod: "GET",
            operationId: null,
            displayName: null);

        // Assert
        result.Should().NotBeNull("route and method should match");
        result!.OperationId.Should().Be("GetToolById");
    }

    /// <summary>
    /// Tests that MatchOperation returns null when no match is found.
    /// </summary>
    [Fact]
    public void MatchOperation_WhenNoMatch_ReturnsNull()
    {
        // Arrange
        var document = CreateTestDocument();
        var logger = new Mock<ILogger<OpenApiOperationMatcher>>().Object;
        var matcher = new OpenApiOperationMatcher(document, logger);

        // Act
        var result = matcher.MatchOperation(
            routeTemplate: "/api/nonexistent",
            httpMethod: "GET",
            operationId: "NonExistent",
            displayName: "NonExistent");

        // Assert
        result.Should().BeNull("no matching operation exists");
    }

    /// <summary>
    /// Tests that MatchByOperationId respects HTTP method filtering.
    /// </summary>
    [Fact]
    public void MatchByOperationId_WithHttpMethodFilter_FiltersByMethod()
    {
        // Arrange
        var document = CreateTestDocumentWithMultipleMethods();
        var logger = new Mock<ILogger<OpenApiOperationMatcher>>().Object;
        var matcher = new OpenApiOperationMatcher(document, logger);

        // Act
        var result = matcher.MatchByOperationId("ToolOperation", "POST");

        // Assert
        result.Should().NotBeNull();
        result!.OperationId.Should().Be("ToolOperation");
        // Verify it's the POST operation, not GET
        document.Paths["/api/tools"].Operations[OperationType.Post].Should().BeSameAs(result);
    }

    /// <summary>
    /// Tests that MatchByRouteAndMethod handles different HTTP methods.
    /// </summary>
    [Theory]
    [InlineData("/api/tools", "GET", "GetTools")]
    [InlineData("/api/tools", "POST", "CreateTool")]
    [InlineData("/api/tools/{id}", "GET", "GetToolById")]
    [InlineData("/api/tools/{id}", "PUT", "UpdateTool")]
    [InlineData("/api/tools/{id}", "DELETE", "DeleteTool")]
    public void MatchByRouteAndMethod_WithDifferentRoutesAndMethods_MatchesCorrectly(
        string route, string method, string expectedOperationId)
    {
        // Arrange
        var document = CreateTestDocument();
        var logger = new Mock<ILogger<OpenApiOperationMatcher>>().Object;
        var matcher = new OpenApiOperationMatcher(document, logger);

        // Act
        var result = matcher.MatchByRouteAndMethod(route, method);

        // Assert
        result.Should().NotBeNull($"operation should be found for {method} {route}");
        result!.OperationId.Should().Be(expectedOperationId);
    }

    /// <summary>
    /// Tests that MatchByRouteAndMethod is case-insensitive for HTTP methods.
    /// </summary>
    [Theory]
    [InlineData("get")]
    [InlineData("GET")]
    [InlineData("Get")]
    public void MatchByRouteAndMethod_IsCaseInsensitiveForHttpMethod(string method)
    {
        // Arrange
        var document = CreateTestDocument();
        var logger = new Mock<ILogger<OpenApiOperationMatcher>>().Object;
        var matcher = new OpenApiOperationMatcher(document, logger);

        // Act
        var result = matcher.MatchByRouteAndMethod("/api/tools", method);

        // Assert
        result.Should().NotBeNull("HTTP method matching should be case-insensitive");
        result!.OperationId.Should().Be("GetTools");
    }

    /// <summary>
    /// Tests that GetUnmatchedOperations returns operations that were not matched.
    /// </summary>
    [Fact]
    public void GetUnmatchedOperations_WhenSomeOperationsMatched_ReturnsUnmatched()
    {
        // Arrange
        var document = CreateTestDocument();
        var logger = new Mock<ILogger<OpenApiOperationMatcher>>().Object;
        var matcher = new OpenApiOperationMatcher(document, logger);

        // Match one operation
        matcher.MatchOperation(
            routeTemplate: "/api/tools",
            httpMethod: "GET",
            operationId: "GetTools",
            displayName: null);

        // Act
        var unmatched = matcher.GetUnmatchedOperations();

        // Assert
        unmatched.Should().NotBeEmpty("some operations were not matched");
        unmatched.Should().NotContain(u => u.OperationId == "GetTools");
        unmatched.Should().Contain(u => u.OperationId == "CreateTool");
    }

    /// <summary>
    /// Tests that path matching normalizes leading/trailing slashes.
    /// </summary>
    [Theory]
    [InlineData("/api/tools")]
    [InlineData("api/tools")]
    [InlineData("/api/tools/")]
    [InlineData("api/tools/")]
    public void MatchByRouteAndMethod_NormalizesPathSlashes(string route)
    {
        // Arrange
        var document = CreateTestDocument();
        var logger = new Mock<ILogger<OpenApiOperationMatcher>>().Object;
        var matcher = new OpenApiOperationMatcher(document, logger);

        // Act
        var result = matcher.MatchByRouteAndMethod(route, "GET");

        // Assert
        result.Should().NotBeNull("path matching should normalize slashes");
        result!.OperationId.Should().Be("GetTools");
    }

    /// <summary>
    /// Creates a test OpenAPI document with various operations for testing.
    /// </summary>
    /// <returns>A test OpenAPI document with sample operations.</returns>
    private static OpenApiDocument CreateTestDocument()
    {
        return new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/api/tools"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation
                        {
                            OperationId = "GetTools",
                            Summary = "Get all tools",
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse()
                            }
                        },
                        [OperationType.Post] = new OpenApiOperation
                        {
                            OperationId = "CreateTool",
                            Summary = "Create a new tool",
                            Responses = new OpenApiResponses
                            {
                                ["201"] = new OpenApiResponse()
                            }
                        }
                    }
                },
                ["/api/tools/{id}"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation
                        {
                            OperationId = "GetToolById",
                            Summary = "Get tool by ID",
                            Parameters = new List<OpenApiParameter>
                            {
                                new()
                                {
                                    Name = "id",
                                    In = ParameterLocation.Path,
                                    Required = true,
                                    Schema = new OpenApiSchema { Type = "string" }
                                }
                            },
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse()
                            }
                        },
                        [OperationType.Put] = new OpenApiOperation
                        {
                            OperationId = "UpdateTool",
                            Summary = "Update tool",
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse()
                            }
                        },
                        [OperationType.Delete] = new OpenApiOperation
                        {
                            OperationId = "DeleteTool",
                            Summary = "Delete tool",
                            Responses = new OpenApiResponses
                            {
                                ["204"] = new OpenApiResponse()
                            }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Creates a test document with multiple methods having the same operation ID.
    /// Used for testing method filtering.
    /// </summary>
    /// <returns>A test OpenAPI document with duplicate operation IDs.</returns>
    private static OpenApiDocument CreateTestDocumentWithMultipleMethods()
    {
        return new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/api/tools"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation
                        {
                            OperationId = "ToolOperation",
                            Summary = "GET tool operation",
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse()
                            }
                        },
                        [OperationType.Post] = new OpenApiOperation
                        {
                            OperationId = "ToolOperation",
                            Summary = "POST tool operation",
                            Responses = new OpenApiResponses
                            {
                                ["201"] = new OpenApiResponse()
                            }
                        }
                    }
                }
            }
        };
    }
}
