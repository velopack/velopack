namespace Divergic.Logging.Xunit;

/// <summary>
///     The <see cref="DefaultScopeFormatter" />
///     class is used to provide log message formatting logic for scope beginning and end messages.
/// </summary>
public class DefaultScopeFormatter : DefaultFormatter
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DefaultScopeFormatter" /> class.
    /// </summary>
    /// <param name="config">The logging configuration.</param>
    public DefaultScopeFormatter(LoggingConfig config) : base(config)
    {
    }

    /// <inheritdoc />
    protected override string FormatMask { get; } = "{0}{3}";
}