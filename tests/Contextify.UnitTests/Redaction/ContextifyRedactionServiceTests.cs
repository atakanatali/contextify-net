using System.Text.Json;
using Contextify.Core.Redaction;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Redaction;

/// <summary>
/// Unit tests for ContextifyRedactionService behavior.
/// Verifies JSON field redaction, text pattern redaction, and safe handling of edge cases.
/// </summary>
public sealed class ContextifyRedactionServiceTests
{
    /// <summary>
    /// Tests that JSON field names are redacted case-insensitively.
    /// </summary>
    [Fact]
    public void RedactJson_WhenFieldMatchesCaseInsensitive_RedactsValue()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: new[] { "password", "secret" },
            redactPatterns: Array.Empty<string>());
        var service = new ContextifyRedactionService(options);

        var json = JsonSerializer.Deserialize<JsonElement>(
            """{"Username":"user1","Password":"hunter2","Secret":"topsecret"}""");

        // Act
        var result = service.RedactJson(json);

        // Assert
        result.Should().NotBeNull();
        result!.Value.TryGetProperty("Username", out var username).Should().BeTrue();
        username.GetString().Should().Be("user1");

        result.Value.TryGetProperty("Password", out var password).Should().BeTrue();
        password.GetString().Should().Be("[REDACTED]");

        result.Value.TryGetProperty("Secret", out var secret).Should().BeTrue();
        secret.GetString().Should().Be("[REDACTED]");
    }

    /// <summary>
    /// Tests that nested objects are recursively redacted.
    /// </summary>
    [Fact]
    public void RedactJson_WhenFieldInNestedObject_RedactsValue()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: new[] { "token" },
            redactPatterns: Array.Empty<string>());
        var service = new ContextifyRedactionService(options);

        var json = JsonSerializer.Deserialize<JsonElement>(
            """{"User":{"Name":"test","Token":"abc123"},"Status":"active"}""");

        // Act
        var result = service.RedactJson(json);

        // Assert
        result.Should().NotBeNull();
        result!.Value.TryGetProperty("User", out var user).Should().BeTrue();
        user.TryGetProperty("Token", out var token).Should().BeTrue();
        token.GetString().Should().Be("[REDACTED]");
    }

    /// <summary>
    /// Tests that arrays are recursively processed for redaction.
    /// </summary>
    [Fact]
    public void RedactJson_WhenFieldInArrayItems_RedactsValues()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: new[] { "apikey" },
            redactPatterns: Array.Empty<string>());
        var service = new ContextifyRedactionService(options);

        var json = JsonSerializer.Deserialize<JsonElement>(
            """{"Items":[{"Name":"item1","ApiKey":"key1"},{"Name":"item2","ApiKey":"key2"}]}""");

        // Act
        var result = service.RedactJson(json);

        // Assert
        result.Should().NotBeNull();
        result!.Value.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);

        var itemArray = items.EnumerateArray().ToList();
        itemArray.Should().HaveCount(2);

        itemArray[0].TryGetProperty("ApiKey", out var key1).Should().BeTrue();
        key1.GetString().Should().Be("[REDACTED]");

        itemArray[1].TryGetProperty("ApiKey", out var key2).Should().BeTrue();
        key2.GetString().Should().Be("[REDACTED]");
    }

    /// <summary>
    /// Tests that redaction is disabled when Enabled is false.
    /// </summary>
    [Fact]
    public void RedactJson_WhenDisabled_ReturnsOriginal()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: false,
            redactJsonFields: new[] { "password" },
            redactPatterns: Array.Empty<string>());
        var service = new ContextifyRedactionService(options);

        var json = JsonSerializer.Deserialize<JsonElement>(
            """{"Password":"secret"}""");

        // Act
        var result = service.RedactJson(json);

        // Assert
        result.Should().Be(json);
    }

    /// <summary>
    /// Tests that null input is handled gracefully.
    /// </summary>
    [Fact]
    public void RedactJson_WhenInputNull_ReturnsNull()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: new[] { "password" },
            redactPatterns: Array.Empty<string>());
        var service = new ContextifyRedactionService(options);

        // Act
        var result = service.RedactJson(null);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that empty field list results in no redaction.
    /// </summary>
    [Fact]
    public void RedactJson_WhenNoFieldsConfigured_ReturnsOriginal()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: Array.Empty<string>(),
            redactPatterns: Array.Empty<string>());
        var service = new ContextifyRedactionService(options);

        var json = JsonSerializer.Deserialize<JsonElement>(
            """{"Password":"secret"}""");

        // Act
        var result = service.RedactJson(json);

        // Assert
        result.Should().Be(json);
    }

    /// <summary>
    /// Tests that text patterns are applied when configured.
    /// </summary>
    [Fact]
    public void RedactText_WhenPatternConfigured_RedactsMatches()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: Array.Empty<string>(),
            redactPatterns: new[] { @"\d{16}" }); // Credit card pattern
        var service = new ContextifyRedactionService(options);

        var input = "Card number: 1234567890123456 expires 12/25";

        // Act
        var result = service.RedactText(input);

        // Assert
        result.Should().Be("Card number: [REDACTED] expires 12/25");
    }

    /// <summary>
    /// Tests that multiple text patterns are applied in order.
    /// </summary>
    [Fact]
    public void RedactText_WhenMultiplePatterns_AppliesInOrder()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: Array.Empty<string>(),
            redactPatterns: new[] { @"\d{3}-\d{2}-\d{4}", @"\d{16}" });
        var service = new ContextifyRedactionService(options);

        var input = "SSN: 123-45-6789 Card: 9876543210987654";

        // Act
        var result = service.RedactText(input);

        // Assert
        result.Should().Be("SSN: [REDACTED] Card: [REDACTED]");
    }

    /// <summary>
    /// Tests that text redaction is disabled when Enabled is false.
    /// </summary>
    [Fact]
    public void RedactText_WhenDisabled_ReturnsOriginal()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: false,
            redactJsonFields: Array.Empty<string>(),
            redactPatterns: new[] { @"\d+" });
        var service = new ContextifyRedactionService(options);

        var input = "Number: 12345";

        // Act
        var result = service.RedactText(input);

        // Assert
        result.Should().Be(input);
    }

    /// <summary>
    /// Tests that null text input is handled gracefully.
    /// </summary>
    [Fact]
    public void RedactText_WhenInputNull_ReturnsNull()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: Array.Empty<string>(),
            redactPatterns: new[] { @"\d+" });
        var service = new ContextifyRedactionService(options);

        // Act
        var result = service.RedactText(null);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that empty text input is handled gracefully.
    /// </summary>
    [Fact]
    public void RedactText_WhenInputEmpty_ReturnsEmpty()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: Array.Empty<string>(),
            redactPatterns: new[] { @"\d+" });
        var service = new ContextifyRedactionService(options);

        // Act
        var result = service.RedactText(string.Empty);

        // Assert
        result.Should().Be(string.Empty);
    }

    /// <summary>
    /// Tests that text without patterns returns unchanged.
    /// </summary>
    [Fact]
    public void RedactText_WhenNoPatternsConfigured_ReturnsOriginal()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: Array.Empty<string>(),
            redactPatterns: Array.Empty<string>());
        var service = new ContextifyRedactionService(options);

        var input = "Some text without patterns";

        // Act
        var result = service.RedactText(input);

        // Assert
        result.Should().Be(input);
    }

    /// <summary>
    /// Tests that complex nested JSON structures are handled correctly.
    /// </summary>
    [Fact]
    public void RedactJson_WhenComplexNestedStructure_RedactsCorrectly()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: new[] { "secret" },
            redactPatterns: Array.Empty<string>());
        var service = new ContextifyRedactionService(options);

        var json = JsonSerializer.Deserialize<JsonElement>(
            """
            {
                "Level1": {
                    "Level2": {
                        "Secret": "hidden",
                        "Array": [
                            {"Secret": "item1"},
                            {"Public": "visible"}
                        ]
                    }
                },
                "Secret": "root"
            }
            """);

        // Act
        var result = service.RedactJson(json);

        // Assert
        result.Should().NotBeNull();
 
         // Check root level
         result!.Value.TryGetProperty("Secret", out var rootSecret).Should().BeTrue();
        rootSecret.GetString().Should().Be("[REDACTED]");

        // Check nested level
        result!.Value.TryGetProperty("Level1", out var level1).Should().BeTrue();
        level1.TryGetProperty("Level2", out var level2).Should().BeTrue();
        level2.TryGetProperty("Secret", out var nestedSecret).Should().BeTrue();
        nestedSecret.GetString().Should().Be("[REDACTED]");

        // Check array items
        level2.TryGetProperty("Array", out var array).Should().BeTrue();
        var items = array.EnumerateArray().ToList();
        items[0].TryGetProperty("Secret", out var itemSecret).Should().BeTrue();
        itemSecret.GetString().Should().Be("[REDACTED]");

        items[1].TryGetProperty("Public", out var itemPublic).Should().BeTrue();
        itemPublic.GetString().Should().Be("visible");
    }

    /// <summary>
    /// Tests that CreateWithFields factory method works correctly.
    /// </summary>
    [Fact]
    public void CreateWithFields_WhenCalled_CreatesEnabledOptions()
    {
        // Act
        var options = ContextifyRedactionOptionsEntity.CreateWithFields("password", "token");

        // Assert
        options.Enabled.Should().BeTrue();
        options.RedactJsonFields.Should().BeEquivalentTo(new[] { "password", "token" });
        options.RedactPatterns.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that CreateWithFieldsAndPatterns factory method works correctly.
    /// </summary>
    [Fact]
    public void CreateWithFieldsAndPatterns_WhenCalled_CreatesEnabledOptions()
    {
        // Act
        var options = ContextifyRedactionOptionsEntity.CreateWithFieldsAndPatterns(
            new[] { "password" },
            new[] { @"\d{16}" });

        // Assert
        options.Enabled.Should().BeTrue();
        options.RedactJsonFields.Should().BeEquivalentTo(new[] { "password" });
        options.RedactPatterns.Should().BeEquivalentTo(new[] { @"\d{16}" });
    }

    /// <summary>
    /// Tests that non-matching field names are not redacted.
    /// </summary>
    [Fact]
    public void RedactJson_WhenFieldDoesNotMatch_KeepsOriginal()
    {
        // Arrange
        var options = new ContextifyRedactionOptionsEntity(
            enabled: true,
            redactJsonFields: new[] { "password" },
            redactPatterns: Array.Empty<string>());
        var service = new ContextifyRedactionService(options);

        var json = JsonSerializer.Deserialize<JsonElement>(
            """{"Username":"user1","Email":"user@example.com"}""");

        // Act
        var result = service.RedactJson(json);

        // Assert
        result.Should().NotBeNull();
        result!.Value.TryGetProperty("Username", out var username).Should().BeTrue();
        username.GetString().Should().Be("user1");

        result.Value.TryGetProperty("Email", out var email).Should().BeTrue();
        email.GetString().Should().Be("user@example.com");
    }
}
