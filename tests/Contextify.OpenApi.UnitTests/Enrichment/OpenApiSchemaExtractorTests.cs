using System.Text.Json;
using Contextify.OpenApi.Enrichment;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Moq;
using Xunit;

namespace Contextify.OpenApi.UnitTests.Enrichment;

/// <summary>
/// Unit tests for OpenApiSchemaExtractor behavior.
/// Verifies schema extraction, type conversion, and description handling.
/// </summary>
public sealed class OpenApiSchemaExtractorTests
{
    /// <summary>
    /// Tests that ExtractInputSchema returns null when operation has no parameters or body.
    /// </summary>
    [Fact]
    public void ExtractInputSchema_WhenNoParametersOrBody_ReturnsNull()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);
        var operation = new OpenApiOperation();

        // Act
        var result = extractor.ExtractInputSchema(operation);

        // Assert
        result.Should().BeNull("operation has no input schema defined");
    }

    /// <summary>
    /// Tests that ExtractInputSchema correctly extracts parameters as properties.
    /// </summary>
    [Fact]
    public void ExtractInputSchema_WithParameters_ReturnsObjectSchemaWithProperties()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);

        var operation = new OpenApiOperation
        {
            Parameters = new List<OpenApiParameter>
            {
                new()
                {
                    Name = "id",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = "string" }
                },
                new()
                {
                    Name = "verbose",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = "boolean" }
                }
            }
        };

        // Act
        var result = extractor.ExtractInputSchema(operation);

        // Assert
        result.Should().NotBeNull();
        result!.Value.ValueKind.Should().Be(JsonValueKind.Object);

        result!.Value.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("object");

        result!.Value.TryGetProperty("properties", out var properties).Should().BeTrue();
        properties.ValueKind.Should().Be(JsonValueKind.Object);

        properties.TryGetProperty("id", out var idProp).Should().BeTrue();
        idProp.TryGetProperty("type", out var idType).Should().BeTrue();
        idType.GetString().Should().Be("string");

        properties.TryGetProperty("verbose", out var verboseProp).Should().BeTrue();
        verboseProp.TryGetProperty("type", out var verboseType).Should().BeTrue();
        verboseType.GetString().Should().Be("boolean");

        result!.Value.TryGetProperty("required", out var required).Should().BeTrue();
        required.EnumerateArray().Select(r => r.GetString()).Should().Contain("id");
    }

    /// <summary>
    /// Tests that ExtractInputSchema merges request body with parameters.
    /// </summary>
    [Fact]
    public void ExtractInputSchema_WithRequestBody_MergesBodySchemaWithParameters()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);

        var operation = new OpenApiOperation
        {
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
            RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["name"] = new OpenApiSchema { Type = "string" },
                                ["age"] = new OpenApiSchema { Type = "integer" }
                            }
                        }
                    }
                }
            }
        };

        // Act
        var result = extractor.ExtractInputSchema(operation);

        // Assert
        result.Should().NotBeNull();
        result!.Value.TryGetProperty("properties", out var properties).Should().BeTrue();

        // Should have both parameter and body properties
        var propertyNames = properties.EnumerateObject().Select(p => p.Name).ToList();
        propertyNames.Should().Contain("id");
        propertyNames.Should().Contain("name");
        propertyNames.Should().Contain("age");
    }

    /// <summary>
    /// Tests that ExtractResponseSchema returns null for operations without responses.
    /// </summary>
    [Fact]
    public void ExtractResponseSchema_WhenNoResponses_ReturnsNull()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);
        var operation = new OpenApiOperation();
        var warnings = new List<string>();

        // Act
        var result = extractor.ExtractResponseSchema(operation, warnings);

        // Assert
        result.Should().BeNull("operation has no responses defined");
    }

    /// <summary>
    /// Tests that ExtractResponseSchema prioritizes 200 response code.
    /// </summary>
    [Fact]
    public void ExtractResponseSchema_With200Response_Returns200Schema()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["result"] = new OpenApiSchema { Type = "string" }
                                }
                            }
                        }
                    }
                },
                ["201"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema { Type = "string" }
                        }
                    }
                }
            }
        };

        var warnings = new List<string>();

        // Act
        var result = extractor.ExtractResponseSchema(operation, warnings);

        // Assert
        result.Should().NotBeNull();
        result!.Value.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("object");
    }

    /// <summary>
    /// Tests that ExtractDescription combines summary and description.
    /// </summary>
    [Fact]
    public void ExtractDescription_WithSummaryAndDescription_CombinesBoth()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);

        var operation = new OpenApiOperation
        {
            Summary = "This is a summary",
            Description = "This is a detailed description"
        };

        // Act
        var result = extractor.ExtractDescription(operation);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("This is a summary");
        result.Should().Contain("This is a detailed description");
    }

    /// <summary>
    /// Tests that ExtractDescription returns null when no description is available.
    /// </summary>
    [Fact]
    public void ExtractDescription_WhenNoDescription_ReturnsNull()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);
        var operation = new OpenApiOperation();

        // Act
        var result = extractor.ExtractDescription(operation);

        // Assert
        result.Should().BeNull("operation has no summary or description");
    }

    /// <summary>
    /// Tests that ConvertSchemaToJson handles primitive types correctly.
    /// </summary>
    [Theory]
    [InlineData("string", "string")]
    [InlineData("integer", "integer")]
    [InlineData("number", "number")]
    [InlineData("boolean", "boolean")]
    [InlineData("array", "array")]
    [InlineData("object", "object")]
    public void ConvertSchemaToJson_WithPrimitiveTypes_ReturnsCorrectJsonSchema(
        string openApiType, string expectedJsonType)
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);

        var schema = new OpenApiSchema { Type = openApiType };

        // Act
        var result = extractor.ConvertSchemaToJson(schema);

        // Assert
        result.Should().NotBeNull();
        result!.Value.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be(expectedJsonType);
    }

    /// <summary>
    /// Tests that ConvertSchemaToJson handles array with items.
    /// </summary>
    [Fact]
    public void ConvertSchemaJson_WithArray_ReturnsItemsSchema()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);

        var schema = new OpenApiSchema
        {
            Type = "array",
            Items = new OpenApiSchema
            {
                Type = "string"
            }
        };

        // Act
        var result = extractor.ConvertSchemaToJson(schema);

        // Assert
        result.Should().NotBeNull();
        result!.Value.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("array");

        result!.Value.TryGetProperty("items", out var items).Should().BeTrue();
        items.TryGetProperty("type", out var itemType).Should().BeTrue();
        itemType.GetString().Should().Be("string");
    }

    /// <summary>
    /// Tests that ConvertSchemaToJson handles object with properties.
    /// </summary>
    [Fact]
    public void ConvertSchemaJson_WithObject_ReturnsProperties()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);

        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["name"] = new OpenApiSchema { Type = "string" },
                ["age"] = new OpenApiSchema { Type = "integer" }
            },
            Required = new HashSet<string> { "name" }
        };

        // Act
        var result = extractor.ConvertSchemaToJson(schema);

        // Assert
        result.Should().NotBeNull();
        result!.Value.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("object");

        result!.Value.TryGetProperty("properties", out var properties).Should().BeTrue();
        properties.ValueKind.Should().Be(JsonValueKind.Object);

        result!.Value.TryGetProperty("required", out var required).Should().BeTrue();
        required.EnumerateArray().Select(r => r.GetString()).Should().Contain("name");
    }

    /// <summary>
    /// Tests that InferAuthRequirement returns true when security is required.
    /// </summary>
    [Fact]
    public void InferAuthRequirement_WithSecurity_ReturnsTrue()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);

        var operation = new OpenApiOperation
        {
            Security = new List<OpenApiSecurityRequirement>
            {
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "bearerAuth"
                        }
                    }] = new List<string>()
                }
            }
        };

        // Act
        var result = extractor.InferAuthRequirement(operation);

        // Assert
        result.Should().BeTrue("operation has security requirement");
    }

    /// <summary>
    /// Tests that InferAuthRequirement returns false when no security is defined.
    /// </summary>
    [Fact]
    public void InferAuthRequirement_WhenNoSecurity_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);
        var operation = new OpenApiOperation();

        // Act
        var result = extractor.InferAuthRequirement(operation);

        // Assert
        result.Should().BeFalse("operation has no security requirement");
    }

    /// <summary>
    /// Tests that InferAuthRequirement returns false for empty security requirement (anonymous).
    /// </summary>
    [Fact]
    public void InferAuthRequirement_WithEmptySecurity_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<OpenApiSchemaExtractor>>().Object;
        var extractor = new OpenApiSchemaExtractor(logger);

        var operation = new OpenApiOperation
        {
            Security = new List<OpenApiSecurityRequirement>
            {
                new()
            }
        };

        // Act
        var result = extractor.InferAuthRequirement(operation);

        // Assert
        result.Should().BeFalse("empty security requirement allows anonymous access");
    }
}
