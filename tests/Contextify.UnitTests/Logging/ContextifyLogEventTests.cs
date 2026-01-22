using Contextify.Logging;
using FluentAssertions;
using System.Diagnostics;
using Xunit;

namespace Contextify.UnitTests.Logging;

/// <summary>
/// Unit tests for ContextifyLogEvent record functionality.
/// Verifies event creation, property merging, and string formatting.
/// </summary>
public sealed class ContextifyLogEventTests
{
    /// <summary>
    /// Tests that Create factory method sets timestamp to UTC now.
    /// </summary>
    [Fact]
    public void Create_WhenCalled_SetsTimestampToUtcNow()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;
        var message = "Test message";

        // Act
        var logEvent = ContextifyLogEvent.Create(ContextifyLogLevel.Information, message);
        var after = DateTimeOffset.UtcNow;

        // Assert
        logEvent.Timestamp.Should().BeOnOrAfter(before);
        logEvent.Timestamp.Should().BeOnOrBefore(after);
    }

    /// <summary>
    /// Tests that Create factory method preserves all parameters.
    /// </summary>
    [Fact]
    public void Create_WhenCalled_PreservesAllParameters()
    {
        // Arrange
        var level = ContextifyLogLevel.Warning;
        var message = "Warning message";
        var category = "TestCategory";
        var exception = new InvalidOperationException("Test exception");
        var properties = new Dictionary<string, object?>
        {
            ["Key1"] = "Value1",
            ["Key2"] = 42
        };
        var eventId = 123;

        // Act
        var logEvent = ContextifyLogEvent.Create(level, message, category, exception, properties, eventId);

        // Assert
        logEvent.Level.Should().Be(level);
        logEvent.Message.Should().Be(message);
        logEvent.Category.Should().Be(category);
        logEvent.Exception.Should().Be(exception);
        logEvent.Properties.Should().BeEquivalentTo(properties);
        logEvent.EventId.Should().Be(eventId);
    }

    /// <summary>
    /// Tests that WithProperties merges new properties into existing properties.
    /// </summary>
    [Fact]
    public void WithProperties_WhenCalled_MergesProperties()
    {
        // Arrange
        var originalProperties = new Dictionary<string, object?>
        {
            ["OriginalKey"] = "OriginalValue"
        };
        var logEvent = ContextifyLogEvent.Create(
            ContextifyLogLevel.Information,
            "Test message",
            properties: originalProperties);
        var additionalProperties = new Dictionary<string, object?>
        {
            ["NewKey"] = "NewValue"
        };

        // Act
        var mergedEvent = logEvent.WithProperties(additionalProperties);

        // Assert
        mergedEvent.Properties.Should().HaveCount(2);
        mergedEvent.Properties.Should().ContainKey("OriginalKey").WhoseValue.Should().Be("OriginalValue");
        mergedEvent.Properties.Should().ContainKey("NewKey").WhoseValue.Should().Be("NewValue");
    }

    /// <summary>
    /// Tests that WithProperties overwrites existing properties with same keys.
    /// </summary>
    [Fact]
    public void WithProperties_WhenKeyExists_OverwritesExistingProperty()
    {
        // Arrange
        var originalProperties = new Dictionary<string, object?>
        {
            ["Key"] = "OriginalValue"
        };
        var logEvent = ContextifyLogEvent.Create(
            ContextifyLogLevel.Information,
            "Test message",
            properties: originalProperties);
        var additionalProperties = new Dictionary<string, object?>
        {
            ["Key"] = "NewValue"
        };

        // Act
        var mergedEvent = logEvent.WithProperties(additionalProperties);

        // Assert
        mergedEvent.Properties.Should().HaveCount(1);
        mergedEvent.Properties["Key"].Should().Be("NewValue");
    }

    /// <summary>
    /// Tests that WithProperties creates a new event without modifying the original.
    /// </summary>
    [Fact]
    public void WithProperties_WhenCalled_DoesNotModifyOriginalEvent()
    {
        // Arrange
        var originalProperties = new Dictionary<string, object?>
        {
            ["Key"] = "OriginalValue"
        };
        var logEvent = ContextifyLogEvent.Create(
            ContextifyLogLevel.Information,
            "Test message",
            properties: originalProperties);
        var originalPropertyCount = logEvent.Properties?.Count ?? 0;

        // Act
        var mergedEvent = logEvent.WithProperties(new Dictionary<string, object?>
        {
            ["NewKey"] = "NewValue"
        });

        // Assert
        logEvent.Properties.Should().HaveCount(originalPropertyCount, "original event should not be modified");
        logEvent.Properties.Should().NotContainKey("NewKey");
        mergedEvent.Properties.Should().HaveCount(originalPropertyCount + 1);
    }

    /// <summary>
    /// Tests that WithProperties works when original event has no properties.
    /// </summary>
    [Fact]
    public void WithProperties_WhenOriginalHasNoProperties_CreatesNewPropertiesDictionary()
    {
        // Arrange
        var logEvent = ContextifyLogEvent.Create(ContextifyLogLevel.Information, "Test message");
        var additionalProperties = new Dictionary<string, object?>
        {
            ["Key"] = "Value"
        };

        // Act
        var mergedEvent = logEvent.WithProperties(additionalProperties);

        // Assert
        mergedEvent.Properties.Should().HaveCount(1);
        mergedEvent.Properties["Key"].Should().Be("Value");
    }

    /// <summary>
    /// Tests that ToString formats event correctly.
    /// </summary>
    [Fact]
    public void ToString_WhenCalled_FormatsEventCorrectly()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 30, 45, TimeSpan.Zero);
        var level = ContextifyLogLevel.Information;
        var message = "Test message";
        var logEvent = new ContextifyLogEvent(timestamp, level, message);

        // Act
        var formatted = logEvent.ToString();

        // Assert
        formatted.Should().Contain("2024-01-15");
        formatted.Should().Contain("10:30:45");
        formatted.Should().Contain("[Information]");
        formatted.Should().Contain(message);
    }

    /// <summary>
    /// Tests that ToString includes category when present.
    /// </summary>
    [Fact]
    public void ToString_WhenCategoryPresent_IncludesCategory()
    {
        // Arrange
        var logEvent = ContextifyLogEvent.Create(
            ContextifyLogLevel.Warning,
            "Test message",
            category: "TestCategory");

        // Act
        var formatted = logEvent.ToString();

        // Assert
        formatted.Should().Contain("[TestCategory]");
    }

    /// <summary>
    /// Tests that ToString includes exception when present.
    /// </summary>
    [Fact]
    public void ToString_WhenExceptionPresent_IncludesException()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        var logEvent = ContextifyLogEvent.Create(
            ContextifyLogLevel.Error,
            "Test message",
            exception: exception);

        // Act
        var formatted = logEvent.ToString();

        // Assert
        formatted.Should().Contain("Test exception");
        formatted.Should().Contain("Exception:");
    }

    /// <summary>
    /// Tests that record equality works correctly for log events.
    /// </summary>
    [Fact]
    public void Equality_WhenSameValues_ReturnsTrue()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        var level = ContextifyLogLevel.Information;
        var message = "Test message";

        // Act
        var event1 = new ContextifyLogEvent(timestamp, level, message);
        var event2 = new ContextifyLogEvent(timestamp, level, message);

        // Assert
        event1.Should().Be(event2);
        (event1 == event2).Should().BeTrue();
    }

    /// <summary>
    /// Tests that record inequality works correctly for different values.
    /// </summary>
    [Fact]
    public void Equality_WhenDifferentValues_ReturnsFalse()
    {
        // Arrange
        var event1 = ContextifyLogEvent.Create(ContextifyLogLevel.Information, "Message 1");
        var event2 = ContextifyLogEvent.Create(ContextifyLogLevel.Warning, "Message 2");

        // Assert
        event1.Should().NotBe(event2);
        (event1 == event2).Should().BeFalse();
    }

    /// <summary>
    /// Tests that TraceId is null when no activity is active.
    /// </summary>
    [Fact]
    public void TraceId_WhenNoActivityActive_ReturnsNull()
    {
        // Arrange
        Activity.Current = null;
        var logEvent = ContextifyLogEvent.Create(ContextifyLogLevel.Information, "Test");

        // Assert
        logEvent.TraceId.Should().BeNull();
    }

    /// <summary>
    /// Tests that TraceId is populated when activity is active.
    /// </summary>
    [Fact]
    public void TraceId_WhenActivityActive_ReturnsTraceId()
    {
        // Arrange
        using var activity = new Activity("TestActivity").Start();
        var logEvent = ContextifyLogEvent.Create(ContextifyLogLevel.Information, "Test");

        // Assert
        logEvent.TraceId.Should().NotBeNull();
        logEvent.TraceId.Should().Be(activity.TraceId.ToString());
        logEvent.SpanId.Should().Be(activity.SpanId.ToString());
    }

    /// <summary>
    /// Tests that ContextifyLogLevel enum values are ordered correctly.
    /// </summary>
    [Fact]
    public void ContextifyLogLevel_Values_AreOrderedCorrectly()
    {
        // Assert
        ContextifyLogLevel.Trace.Should().Be((ContextifyLogLevel)0);
        ContextifyLogLevel.Debug.Should().Be((ContextifyLogLevel)1);
        ContextifyLogLevel.Information.Should().Be((ContextifyLogLevel)2);
        ContextifyLogLevel.Warning.Should().Be((ContextifyLogLevel)3);
        ContextifyLogLevel.Error.Should().Be((ContextifyLogLevel)4);
        ContextifyLogLevel.Critical.Should().Be((ContextifyLogLevel)5);
    }
}
