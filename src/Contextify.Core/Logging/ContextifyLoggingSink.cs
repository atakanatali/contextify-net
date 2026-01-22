using Contextify.Logging;

namespace Contextify.Core.Logging;

/// <summary>
/// Adapter sink that wraps an IContextifyLogging implementation.
/// Provides a bridge between the IContextifyLogSink interface and custom
/// IContextifyLogging implementations registered in the DI container.
/// </summary>
internal sealed class ContextifyLoggingSink : IContextifyLogSink
{
    private readonly IContextifyLogging _logging;

    /// <summary>
    /// Initializes a new instance of the ContextifyLoggingSink class.
    /// </summary>
    /// <param name="logging">The IContextifyLogging implementation to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when logging is null.</exception>
    public ContextifyLoggingSink(IContextifyLogging logging)
    {
        _logging = logging ?? throw new ArgumentNullException(nameof(logging));
    }

    /// <summary>
    /// Writes a log event to the underlying IContextifyLogging implementation.
    /// </summary>
    /// <param name="evt">The log event to write.</param>
    public void Write(ContextifyLogEvent evt)
    {
        if (evt is null)
        {
            return;
        }

        try
        {
            _logging.Log(evt);
        }
        catch
        {
            // Silently ignore logging failures
        }
    }

    /// <summary>
    /// Checks whether logging at the specified level is enabled.
    /// </summary>
    /// <param name="level">The log level to check.</param>
    /// <returns>True if logging at the specified level is enabled; otherwise, false.</returns>
    public bool IsEnabled(ContextifyLogLevel level)
    {
        try
        {
            return _logging.IsEnabled(level);
        }
        catch
        {
            return false;
        }
    }
}
