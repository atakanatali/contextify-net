namespace Contextify.Config.AppSettings.Options;

/// <summary>
/// Configuration options for binding Contextify policy configuration from application settings.
/// Defines the configuration section name and binding behavior for appsettings.json-based policy loading.
/// Enables centralized configuration management through standard .NET configuration patterns.
/// </summary>
public sealed class ContextifyAppSettingsOptionsEntity
{
    /// <summary>
    /// Gets or sets the configuration section name to bind for policy configuration.
    /// Determines which section in appsettings.json contains the Contextify policy settings.
    /// Default value is "Contextify" for consistency with the framework naming.
    /// The section should contain policy configuration in the expected JSON structure.
    /// </summary>
    /// <remarks>
    /// The configuration section structure should match the ContextifyPolicyConfigDto schema:
    /// <code>
    /// {
    ///   "Contextify": {
    ///     "Policy": {
    ///       "denyByDefault": true,
    ///       "whitelist": [...],
    ///       "blacklist": [...]
    ///     }
    ///   }
    /// }
    /// </code>
    /// When using a custom section name, ensure the configuration structure is maintained.
    /// </remarks>
    public string ConfigurationSectionName { get; set; } = "Contextify";

    /// <summary>
    /// Gets or sets a value indicating whether to throw on configuration binding errors.
    /// When true, invalid configuration will cause immediate startup failure.
    /// When false, binding errors are logged and default configuration is used.
    /// Default value is true for fail-fast behavior in production environments.
    /// </summary>
    public bool ThrowOnBindingError { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to watch for configuration file changes.
    /// When true, configuration reload is triggered when the underlying file changes.
    /// When false, configuration is loaded once at startup and never refreshed.
    /// Default value is true for dynamic configuration updates.
    /// </summary>
    public bool EnableReload { get; set; } = true;

    /// <summary>
    /// Gets or sets the reload delay in milliseconds after a file change is detected.
    /// Prevents excessive reloading due to rapid successive file edits.
    /// Default value is 250ms for balanced responsiveness and stability.
    /// </summary>
    public int ReloadDelayMs { get; set; } = 250;

    /// <summary>
    /// Validates the appsettings options configuration.
    /// Ensures all settings are within acceptable ranges and properly configured.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConfigurationSectionName))
        {
            throw new InvalidOperationException(
                $"{nameof(ConfigurationSectionName)} cannot be null or empty.");
        }

        if (ReloadDelayMs < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(ReloadDelayMs)} cannot be negative. " +
                $"Provided value: {ReloadDelayMs}");
        }

        if (ReloadDelayMs > 30000)
        {
            throw new InvalidOperationException(
                $"{nameof(ReloadDelayMs)} cannot exceed 30000ms (30 seconds). " +
                $"Provided value: {ReloadDelayMs}");
        }
    }
}
