using Xunit;
using FluentAssertions;
using System.IO;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Contextify.UnitTests;

/// <summary>
/// Unit tests for GitHub release notes configuration validation.
/// Validates the structure and content of .github/release.yml file.
/// </summary>
public sealed class ReleaseConfigurationTests
{
    private const string ReleaseConfigPath = ".github/release.yml";

    /// <summary>
    /// Tests that the release.yml configuration file exists.
    /// </summary>
    [Fact]
    public void ReleaseConfigurationFile_Should_Exist()
    {
        // Arrange
        var repositoryRoot = GetRepositoryRoot();
        var configPath = Path.Combine(repositoryRoot, ReleaseConfigPath);

        // Act & Assert
        File.Exists(configPath).Should().BeTrue("release.yml configuration file must exist for automated release notes generation");
    }

    /// <summary>
    /// Tests that the release.yml file contains all required categories.
    /// </summary>
    [Fact]
    public void ReleaseConfiguration_Should_ContainAllRequiredCategories()
    {
        // Arrange
        var configContent = ReadReleaseConfiguration();
        var requiredCategories = new[]
        {
            "Breaking Changes",
            "Added",
            "Changed",
            "Fixed",
            "Security"
        };

        // Act & Assert
        foreach (var category in requiredCategories)
        {
            configContent.Should().Contain(category, $"release.yml must contain '{category}' category");
        }
    }

    /// <summary>
    /// Tests that the release.yml file contains all required label mappings.
    /// </summary>
    [Fact]
    public void ReleaseConfiguration_Should_ContainAllRequiredLabels()
    {
        // Arrange
        var configContent = ReadReleaseConfiguration();
        var requiredLabels = new[]
        {
            "type:breaking",
            "type:feature",
            "type:change",
            "type:fix",
            "type:security"
        };

        // Act & Assert
        foreach (var label in requiredLabels)
        {
            configContent.Should().Contain(label, $"release.yml must contain '{label}' label mapping");
        }
    }

    /// <summary>
    /// Tests that the release.yml file contains CHANGELOG.md reference.
    /// </summary>
    [Fact]
    public void ReleaseConfiguration_Should_ReferenceChangelog()
    {
        // Arrange
        var configContent = ReadReleaseConfiguration();

        // Act & Assert
        configContent.Should().Contain("CHANGELOG.md", "release.yml should reference CHANGELOG.md for detailed release notes");
    }

    /// <summary>
    /// Tests that the release.yml file contains skip-release-notes exclude label.
    /// </summary>
    [Fact]
    public void ReleaseConfiguration_Should_ContainSkipReleaseNotesLabel()
    {
        // Arrange
        var configContent = ReadReleaseConfiguration();

        // Act & Assert
        configContent.Should().Contain("skip-release-notes", "release.yml should support excluding PRs from release notes");
    }

    /// <summary>
    /// Tests that the release.yml file is valid YAML.
    /// </summary>
    [Fact]
    public void ReleaseConfiguration_Should_BeValidYaml()
    {
        // Arrange
        var configContent = ReadReleaseConfiguration();
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        // Act
        Action act = () => deserializer.Deserialize<Dictionary<string, object>>(configContent);

        // Assert
        act.Should().NotThrow("release.yml must be valid YAML syntax");
    }

    /// <summary>
    /// Tests that label colors are valid hex colors.
    /// </summary>
    [Fact]
    public void ReleaseConfiguration_LabelColors_Should_BeValidHexFormat()
    {
        // Arrange
        var configContent = ReadReleaseConfiguration();
        var hexColorPattern = new System.Text.RegularExpressions.Regex(@"#[0-9a-fA-F]{6}");

        // Act
        var colorMatches = hexColorPattern.Matches(configContent);

        // Assert
        colorMatches.Count.Should().BeGreaterThan(0, "release.yml should define label colors");

        foreach (System.Text.RegularExpressions.Match match in colorMatches)
        {
            var color = match.Value;
            color.Should().MatchRegex(@"^#[0-9a-fA-F]{6}$", $"label color '{color}' must be valid 6-digit hex format");
        }
    }

    /// <summary>
    /// Tests that the release.yml file contains label definitions.
    /// </summary>
    [Fact]
    public void ReleaseConfiguration_Should_ContainLabelDefinitions()
    {
        // Arrange
        var configContent = ReadReleaseConfiguration();

        // Act & Assert
        configContent.Should().Contain("label_config", "release.yml should define label configurations");
        configContent.Should().Contain("name:", "label definitions should have names");
        configContent.Should().Contain("color:", "label definitions should have colors");
        configContent.Should().Contain("description:", "label definitions should have descriptions");
    }

    /// <summary>
    /// Reads the release configuration file content.
    /// </summary>
    private static string ReadReleaseConfiguration()
    {
        var repositoryRoot = GetRepositoryRoot();
        var configPath = Path.Combine(repositoryRoot, ReleaseConfigPath);

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Release configuration file not found: {configPath}");
        }

        return File.ReadAllText(configPath);
    }

    /// <summary>
    /// Gets the repository root directory by traversing up from the current directory.
    /// </summary>
    private static string GetRepositoryRoot()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var directoryInfo = new DirectoryInfo(currentDirectory);

        // Traverse up to find the directory containing .github folder
        while (directoryInfo != null && !Directory.Exists(Path.Combine(directoryInfo.FullName, ".github")))
        {
            directoryInfo = directoryInfo.Parent;
        }

        return directoryInfo?.FullName ?? currentDirectory;
    }
}
