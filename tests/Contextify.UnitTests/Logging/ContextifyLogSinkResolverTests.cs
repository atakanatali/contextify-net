using Contextify.Core.Builder;
using Contextify.Core.Extensions;
using Contextify.Core.Logging;
using Contextify.Logging;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Contextify.UnitTests.Logging;

/// <summary>
/// Unit tests for ContextifyLogSinkResolver functionality.
/// Verifies correct sink selection based on available services.
/// </summary>
public sealed class ContextifyLogSinkResolverTests
{
    /// <summary>
    /// Tests that when IContextifyLogging is registered, the resolver uses it.
    /// Custom logging implementation should take priority over ILogger and Console.
    /// </summary>
    [Fact]
    public void Resolve_WhenIContextifyLoggingIsRegistered_ReturnsContextifyLoggingSink()
    {
        // Arrange
        var services = new ServiceCollection();
        var customLogging = new CustomContextifyLogging();
        services.AddSingleton<IContextifyLogging>(customLogging);
        services.AddLogging(); // Also register ILoggerFactory
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var sink = ContextifyLogSinkResolver.Resolve(serviceProvider);

        // Assert
        sink.Should().NotBeNull();
        sink.Should().BeOfType<ContextifyLoggingSink>("custom IContextifyLogging should be prioritized");

        // Verify the custom logging was used
        var logEvent = ContextifyLogEvent.Create(ContextifyLogLevel.Information, "Test message");
        sink.Write(logEvent);
        customLogging.LoggedEvents.Should().ContainSingle().Which.Message.Should().Be("Test message");
    }

    /// <summary>
    /// Tests that when IContextifyLogging is not registered but ILoggerFactory exists,
    /// the resolver uses the ILogger-based sink.
    /// </summary>
    [Fact]
    public void Resolve_WhenIContextifyLoggingNotRegisteredButLoggerFactoryExists_ReturnsILoggerLogSink()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var sink = ContextifyLogSinkResolver.Resolve(serviceProvider);

        // Assert
        sink.Should().NotBeNull();
        sink.Should().BeOfType<ILoggerLogSink>("ILogger should be used when custom logging is not registered");
    }

    /// <summary>
    /// Tests that when neither IContextifyLogging nor ILoggerFactory exists,
    /// the resolver uses the Console sink fallback without throwing.
    /// </summary>
    [Fact]
    public void Resolve_WhenNeitherIContextifyLoggingNorLoggerFactoryExists_ReturnsConsoleLogSink()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var sink = ContextifyLogSinkResolver.Resolve(serviceProvider);

        // Assert
        sink.Should().NotBeNull();
        sink.Should().BeOfType<ConsoleLogSink>("Console should be used as fallback when no logging is configured");

        // Verify the sink works without throwing
        var logEvent = ContextifyLogEvent.Create(ContextifyLogLevel.Information, "Test message");
        Action act = () => sink.Write(logEvent);
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that HasContextifyLogging returns true when IContextifyLogging is registered.
    /// </summary>
    [Fact]
    public void HasContextifyLogging_WhenIContextifyLoggingIsRegistered_ReturnsTrue()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IContextifyLogging, CustomContextifyLogging>();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var hasCustomLogging = ContextifyLogSinkResolver.HasContextifyLogging(serviceProvider);

        // Assert
        hasCustomLogging.Should().BeTrue("IContextifyLogging should be detected as registered");
    }

    /// <summary>
    /// Tests that HasContextifyLogging returns false when IContextifyLogging is not registered.
    /// </summary>
    [Fact]
    public void HasContextifyLogging_WhenIContextifyLoggingNotRegistered_ReturnsFalse()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var hasCustomLogging = ContextifyLogSinkResolver.HasContextifyLogging(serviceProvider);

        // Assert
        hasCustomLogging.Should().BeFalse("IContextifyLogging should not be detected when not registered");
    }

    /// <summary>
    /// Tests that HasLoggerFactory returns true when ILoggerFactory is registered.
    /// </summary>
    [Fact]
    public void HasLoggerFactory_WhenLoggerFactoryIsRegistered_ReturnsTrue()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var hasLoggerFactory = ContextifyLogSinkResolver.HasLoggerFactory(serviceProvider);

        // Assert
        hasLoggerFactory.Should().BeTrue("ILoggerFactory should be detected as registered");
    }

    /// <summary>
    /// Tests that ResolveAsync returns the same sink as Resolve.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenCalled_ReturnsSameSinkAsResolve()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var syncSink = ContextifyLogSinkResolver.Resolve(serviceProvider);
        var asyncSink = await ContextifyLogSinkResolver.ResolveAsync(serviceProvider);

        // Assert
        asyncSink.Should().NotBeNull();
        asyncSink.GetType().Should().Be(syncSink.GetType(), "async and sync resolve should return same sink type");
    }

    /// <summary>
    /// Tests that ResolveAsync throws TaskCanceledException when cancellation is requested.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenCancellationRequested_ThrowsTaskCanceledException()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await ContextifyLogSinkResolver.ResolveAsync(serviceProvider, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    /// <summary>
    /// Tests that Resolve with minimum level filters out log events below the threshold.
    /// </summary>
    [Fact]
    public void Resolve_WithMinimumLevel_FiltersOutLowerLevelEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var sink = ContextifyLogSinkResolver.Resolve(serviceProvider, ContextifyLogLevel.Warning);

        // Assert
        sink.IsEnabled(ContextifyLogLevel.Trace).Should().BeFalse();
        sink.IsEnabled(ContextifyLogLevel.Debug).Should().BeFalse();
        sink.IsEnabled(ContextifyLogLevel.Information).Should().BeFalse();
        sink.IsEnabled(ContextifyLogLevel.Warning).Should().BeTrue();
        sink.IsEnabled(ContextifyLogLevel.Error).Should().BeTrue();
        sink.IsEnabled(ContextifyLogLevel.Critical).Should().BeTrue();
    }

    /// <summary>
    /// Tests that Resolve throws ArgumentNullException when serviceProvider is null.
    /// </summary>
    [Fact]
    public void Resolve_WhenServiceProviderIsNull_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => ContextifyLogSinkResolver.Resolve(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serviceProvider");
    }

    /// <summary>
    /// Tests that HasContextifyLogging throws ArgumentNullException when serviceProvider is null.
    /// </summary>
    [Fact]
    public void HasContextifyLogging_WhenServiceProviderIsNull_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => ContextifyLogSinkResolver.HasContextifyLogging(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serviceProvider");
    }


    /// <summary>
    /// Custom implementation of IContextifyLogging for testing.
    /// </summary>
    private sealed class CustomContextifyLogging : IContextifyLogging
    {
        private readonly List<ContextifyLogEvent> _loggedEvents = new();
        private readonly ContextifyLogLevel _minimumLevel = ContextifyLogLevel.Trace;

        public IReadOnlyList<ContextifyLogEvent> LoggedEvents => _loggedEvents;

        public bool IsEnabled(ContextifyLogLevel level) => level >= _minimumLevel;

        public void Log(ContextifyLogEvent evt)
        {
            if (evt is not null && IsEnabled(evt.Level))
            {
                _loggedEvents.Add(evt);
            }
        }
    }
}
