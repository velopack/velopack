using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog.Events;

namespace Velopack.Vpk.Logging;

static class LevelStyle
{
    public static string GetLevelHighlight(LogEvent logEvent)
    {
        var levelMoniker = logEvent.Level.ToString().ToUpper().Substring(0, 3);
        return logEvent.Level switch {
            LogEventLevel.Verbose => LevelStyle.HighlightVerbose(levelMoniker),
            LogEventLevel.Debug => LevelStyle.HighlightDebug(levelMoniker),
            LogEventLevel.Information => LevelStyle.HighlightInfo(levelMoniker),
            LogEventLevel.Warning => LevelStyle.HighlightWarning(levelMoniker),
            LogEventLevel.Error => LevelStyle.HighlightError(levelMoniker),
            LogEventLevel.Fatal => LevelStyle.HighlightFatal(levelMoniker),
            _ => levelMoniker,
        };
    }

    internal static string HighlightProp(string text)
    {
        return $"[lime]{text}[/]";
    }

    internal static string HighlightMuted(string text)
    {
        return $"[grey]{text}[/]";
    }

    internal static string HighlightVerbose(string text)
    {
        return HighlightMuted(text);
    }

    internal static string HighlightDebug(string text)
    {
        return $"[silver]{text}[/]";
    }

    internal static string HighlightInfo(string text)
    {
        return $"[deepskyblue1]{text}[/]";
    }

    internal static string HighlightWarning(string text)
    {
        return $"[yellow]{text}[/]";
    }

    internal static string HighlightError(string text)
    {
        return $"[red]{text}[/]";
    }

    internal static string HighlightFatal(string text)
    {
        return $"[maroon]{text}[/]";
    }
}
