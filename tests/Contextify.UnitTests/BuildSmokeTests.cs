using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests;

/// <summary>
/// Smoke tests to verify the test project builds and executes correctly.
/// These tests serve as a baseline to ensure the testing infrastructure is properly configured.
/// </summary>
public sealed class BuildSmokeTests
{
    /// <summary>
    /// Verifies that the testing framework is properly initialized and can execute a basic test.
    /// This is a minimal smoke test that confirms xUnit is working correctly.
    /// </summary>
    [Fact]
    public void SmokeTest_BuildExecution_ExpectedResult_Passes()
    {
        // Arrange & Act & Assert
        // This test passes if it executes without throwing an exception,
        // confirming that the testing infrastructure is properly set up.
        Assert.True(true, "Smoke test executed successfully");
    }

    /// <summary>
    /// Verifies that FluentAssertions is properly referenced and functional.
    /// Ensures assertion extensions are available for use in tests.
    /// </summary>
    [Fact]
    public void FluentAssertions_SmokeTest_ExpectedResult_Passes()
    {
        // Arrange & Act & Assert
        // Using FluentAssertions to verify the library is correctly integrated
        true.Should().BeTrue("FluentAssertions is properly configured");
    }

    /// <summary>
    /// Verifies that the Contextify.UnitTests namespace and assembly are correctly loaded.
    /// This test validates the project structure and naming conventions.
    /// </summary>
    [Fact]
    public void Assembly_WhenLoaded_HasCorrectNamespace()
    {
        // Arrange
        var expectedNamespace = "Contextify.UnitTests";

        // Act
        var actualNamespace = typeof(BuildSmokeTests).Namespace;

        // Assert
        actualNamespace.Should().Be(expectedNamespace, "namespace should match project naming convention");
    }
}
