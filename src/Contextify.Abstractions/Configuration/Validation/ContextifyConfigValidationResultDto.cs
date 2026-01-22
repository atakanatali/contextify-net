using System;
using System.Collections.Generic;
using System.Linq;

namespace Contextify.Config.Abstractions.Validation;

/// <summary>
/// Data transfer object representing the result of a configuration validation operation.
/// Contains validation warnings and errors discovered during validation checks.
/// Warnings indicate potential issues but do not prevent configuration usage.
/// Errors indicate critical issues that may affect system behavior.
/// </summary>
public sealed record ContextifyConfigValidationResultDto
{
    /// <summary>
    /// Gets the collection of validation warnings.
    /// Warnings indicate non-critical issues that should be reviewed but do not prevent configuration usage.
    /// Examples include deprecated field combinations or recommended best practice violations.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets the collection of validation errors.
    /// Errors indicate critical issues that may affect system behavior or cause runtime failures.
    /// Examples include missing required fields, invalid value ranges, or contradictory settings.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the validation passed without any errors.
    /// Returns true if there are no errors; warnings do not affect this property.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets a value indicating whether the validation produced any output (warnings or errors).
    /// Returns true if there is at least one warning or error.
    /// </summary>
    public bool HasMessages => Warnings.Count > 0 || Errors.Count > 0;

    /// <summary>
    /// Creates a validation result with no warnings or errors.
    /// Represents a completely successful validation.
    /// </summary>
    /// <returns>A new validation result indicating success.</returns>
    public static ContextifyConfigValidationResultDto Success() =>
        new()
        {
            Warnings = [],
            Errors = []
        };

    /// <summary>
    /// Creates a validation result with the specified warnings and no errors.
    /// </summary>
    /// <param name="warnings">The collection of warning messages.</param>
    /// <returns>A new validation result with warnings.</returns>
    public static ContextifyConfigValidationResultDto WithWarnings(IEnumerable<string> warnings) =>
        new()
        {
            Warnings = warnings.ToList().AsReadOnly(),
            Errors = []
        };

    /// <summary>
    /// Creates a validation result with the specified errors and no warnings.
    /// </summary>
    /// <param name="errors">The collection of error messages.</param>
    /// <returns>A new validation result with errors.</returns>
    public static ContextifyConfigValidationResultDto WithErrors(IEnumerable<string> errors) =>
        new()
        {
            Warnings = [],
            Errors = errors.ToList().AsReadOnly()
        };

    /// <summary>
    /// Creates a validation result with both warnings and errors.
    /// </summary>
    /// <param name="warnings">The collection of warning messages.</param>
    /// <param name="errors">The collection of error messages.</param>
    /// <returns>A new validation result with both warnings and errors.</returns>
    public static ContextifyConfigValidationResultDto WithWarningsAndErrors(
        IEnumerable<string> warnings,
        IEnumerable<string> errors) =>
        new()
        {
            Warnings = warnings.ToList().AsReadOnly(),
            Errors = errors.ToList().AsReadOnly()
        };

    /// <summary>
    /// Returns a formatted string representation of the validation result.
    /// Useful for logging and debugging validation issues.
    /// </summary>
    /// <returns>A formatted string containing all warnings and errors.</returns>
    public string FormatMessages()
    {
        if (!HasMessages)
        {
            return "Validation passed: No warnings or errors.";
        }

        var parts = new List<string>();

        if (Warnings.Count > 0)
        {
            parts.Add($"Warnings ({Warnings.Count}):");
            foreach (var warning in Warnings)
            {
                parts.Add($"  - {warning}");
            }
        }

        if (Errors.Count > 0)
        {
            parts.Add($"Errors ({Errors.Count}):");
            foreach (var error in Errors)
            {
                parts.Add($"  - {error}");
            }
        }

        return string.Join(Environment.NewLine, parts);
    }
}
