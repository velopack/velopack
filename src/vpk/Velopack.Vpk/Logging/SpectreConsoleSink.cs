using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Velopack.Vpk.Logging;

public class SpectreConsoleSink : ILogEventSink
{
    private readonly string _dirtmp;

    public SpectreConsoleSink()
    {
        _dirtmp = Path.GetTempPath();
    }

    public void Emit(LogEvent logEvent)
    {
        string prefix = $"[[{logEvent.Timestamp:HH:mm:ss} {LevelStyle.GetLevelHighlight(logEvent)}]] ";
        string message = logEvent.RenderMessage();

        if (VelopackRuntimeInfo.IsWindows) {
            message = message.Replace("\r", "");
            message = message.Replace(_dirtmp, "%TEMP%\\");
        }

        List<IRenderable> renderables = new List<IRenderable>();

        if (message.Contains('\n') || Markup.Remove(prefix + message).Length > Console.WindowWidth) {
            renderables.Add(new Markup(prefix + Environment.NewLine));
            try {
                renderables.Add(new Padder(new Markup(message), new Padding(4, 0, 0, 0)));
            } catch {
                // if we fail to parse markup, fallback to plain text
                renderables.Add(new Padder(new Text(Markup.Remove(message)), new Padding(4, 0, 0, 0)));
            }
        } else {
            try {
                renderables.Add(new Markup(prefix + message + Environment.NewLine));
            } catch {
                // if we fail to parse markup, fallback to plain text
                renderables.Add(new Markup(prefix + Markup.Remove(message) + Environment.NewLine));
            }
        }

        if (logEvent.Exception != null) {
            renderables.Add(new Padder(logEvent.Exception.GetRenderable(ExceptionFormats.ShortenEverything), new Padding(4, 0, 0, 0)));
        }

        AnsiConsole.Write(new RenderableCollection(renderables));
    }
}

public static class SpectreConsoleSinkExtensions
{
    public static LoggerConfiguration Spectre(
        this LoggerSinkConfiguration loggerConfiguration,
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
        LoggingLevelSwitch levelSwitch = null)
    {
        return loggerConfiguration.Sink(new SpectreConsoleSink(), restrictedToMinimumLevel, levelSwitch);
    }
}
