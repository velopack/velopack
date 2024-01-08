using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Parsing;
using Serilog.Sinks.Spectre.Extensions;
using Serilog.Sinks.Spectre.Renderers;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Velopack.Vpk
{
    public class MySpectreConsoleSink : ILogEventSink
    {
        readonly ITemplateTokenRenderer[] renderers;

        public MySpectreConsoleSink(string outputTemplate)
        {
            this.renderers = InitializeRenders(outputTemplate).ToArray();
        }

        public void Emit(LogEvent logEvent)
        {
            // Create renderable objects for each
            // defined token
            IRenderable[] items = this.renderers
                .SelectMany(r => r.Render(logEvent))
                .ToArray();

            // Join all renderable objects
            RenderableCollection collection = new RenderableCollection(items);

            // Write them to the console
            global::Spectre.Console.AnsiConsole.Write(collection);
        }

        private static IEnumerable<ITemplateTokenRenderer> InitializeRenders(string outputTemplate)
        {
            var template = new MessageTemplateParser().Parse(outputTemplate);

            foreach (MessageTemplateToken token in template.Tokens) {
                ITemplateTokenRenderer renderer;
                if (TryInitializeRender(token, out renderer)) {
                    yield return renderer;
                }
            }
        }

        private static bool TryInitializeRender(
            MessageTemplateToken token,
            out ITemplateTokenRenderer renderer)
        {
            if (token is TextToken tt) {
                renderer = new TextTokenRenderer(tt.Text);
                return true;
            }

            if (token is PropertyToken pt) {
                return TryInitializePropertyRender(pt, out renderer);
            }

            renderer = null;
            return false;
        }

        private static bool TryInitializePropertyRender(
            PropertyToken propertyToken,
            out ITemplateTokenRenderer renderer)
        {
            renderer = GetPropertyRender(propertyToken);
            return renderer != null;
        }

        private static ITemplateTokenRenderer GetPropertyRender(PropertyToken propertyToken)
        {
            switch (propertyToken.PropertyName) {
            case OutputProperties.LevelPropertyName: {
                    return new LevelTokenRenderer(propertyToken);
                }
            case OutputProperties.NewLinePropertyName: {
                    return new NewLineTokenRenderer();
                }
            case OutputProperties.ExceptionPropertyName: {
                    return new MyExceptionTokenRenderer();
                }
            case OutputProperties.MessagePropertyName: {
                    return new MessageTemplateOutputTokenRenderer(propertyToken);
                }
            case OutputProperties.TimestampPropertyName: {
                    return new TimestampTokenRenderer(propertyToken);
                }
            case OutputProperties.PropertiesPropertyName: {
                    return new PropertyTemplateRenderer(propertyToken);
                }
            default: {
                    return new EventPropertyTokenRenderer(propertyToken);
                }
            }
        }
    }

    public class MyExceptionTokenRenderer : ITemplateTokenRenderer
    {
        public IEnumerable<IRenderable> Render(LogEvent logEvent)
        {
            if (logEvent.Exception != null) {
                yield return logEvent.Exception.GetRenderable(ExceptionFormats.ShortenEverything);
            }
        }
    }

    public static class MySpectreConsoleSinkExtensions
    {
        const string DefaultConsoleOutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

        /// <summary>
        /// Write log events to the console using Spectre.Console.
        /// </summary>
        /// <param name="loggerConfiguration">Logger sink configuration.</param>
        /// <param name="outputTemplate">A message template describing the format used to write to the sink.
        /// The default is "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}".</param>
        /// <param name="restrictedToMinimumLevel">The minimum level for
        /// events passed through the sink. Ignored when <paramref name="levelSwitch"/> is specified.</param>
        /// <param name="levelSwitch">A switch allowing the pass-through minimum level
        /// to be changed at runtime.</param>
        /// <returns>Configuration object allowing method chaining.</returns>
        public static LoggerConfiguration SpectreShortenedExceptions(
            this LoggerSinkConfiguration loggerConfiguration,
            string outputTemplate = DefaultConsoleOutputTemplate,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            LoggingLevelSwitch levelSwitch = null)
        {
            return loggerConfiguration.Sink(new MySpectreConsoleSink(outputTemplate), restrictedToMinimumLevel, levelSwitch);
        }
    }
}
