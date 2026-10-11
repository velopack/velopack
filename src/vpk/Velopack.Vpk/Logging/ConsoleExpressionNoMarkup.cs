using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Expressions;
using Serilog.Templates;
using Serilog.Templates.Themes;
using Spectre.Console;

namespace Velopack.Vpk.Logging;

public static class ConsoleExpressionNoMarkup
{
    public static void ConsoleNoMarkup(this LoggerSinkConfiguration conf, LogOutputSwitch output)
    {
        var myFunctions = new StaticMemberNameResolver(typeof(ConsoleExpressionNoMarkup));
        var template = new ExpressionTemplate("[{@t:HH:mm:ss} {@l:u3}] {NoMarkup(@m)}\n{@x}", nameResolver: myFunctions, theme: TemplateTheme.Literate);

        // the console sink's output stream is fixed when it is created, so one sink per stream
        // is registered and the switch selects between them per event.
        conf.Conditional(_ => !output.UseStandardError, c => c.Console(template));
        conf.Conditional(_ => output.UseStandardError, c => c.Console(template, standardErrorFromLevel: LevelAlias.Minimum));
    }

    public static LogEventPropertyValue NoMarkup(LogEventPropertyValue message)
    {
        try {
            if (message is ScalarValue sv && sv.Value is string s) {
                return new ScalarValue(Markup.Remove(s));
            }
        } catch { }
        return message;
    }
}
