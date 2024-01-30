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
    public static void ConsoleNoMarkup(this LoggerSinkConfiguration conf)
    {
        var myFunctions = new StaticMemberNameResolver(typeof(ConsoleExpressionNoMarkup));
        conf.Console(new ExpressionTemplate("[{@t:HH:mm:ss} {@l:u3}] {NoMarkup(@m)}\n{@x}", nameResolver: myFunctions, theme: TemplateTheme.Literate));
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
