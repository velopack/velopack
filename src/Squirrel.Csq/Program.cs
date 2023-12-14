using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Serilog.Events;
using Serilog;
using CliFx;

namespace Squirrel.Csq;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings {
            Args = args,
            ApplicationName = "My App",
            EnvironmentName = "Production",
            ContentRootPath = Environment.CurrentDirectory,
            Configuration = new ConfigurationManager(),
        });

        var host = builder.Build();

        host.Services.


        return await new CliApplicationBuilder()
            .AddCommandsFromThisAssembly()
            .Build()
            .RunAsync()
            .ConfigureAwait(false);
    }

    private static Parser CreateParser()
    {
        //var rootCommand = new RootCommand("Bicep registry module tool")
        //    .AddSubcommand(new ValidateCommand())
        //    .AddSubcommand(new GenerateCommand());

        var parser = new CliConfiguration(null)
            .UseHost(Host.CreateDefaultBuilder, ConfigureHost)
            .UseDefaults()
            .UseVerboseOption()
            .Build();

        // Have to use parser.Invoke instead of rootCommand.Invoke due to the
        // System.CommandLine bug: https://github.com/dotnet/command-line-api/issues/1691.
        rootCommand.Handler = CommandHandler.Create(() => parser.Invoke("-h"));

        return parser;
    }

    private static IHostBuilder CreateBuilder(string[] args)
    {

    }

    private static void ConfigureHost(IHostBuilder builder)
    {
        builder.UseSerilog((context, logging) => logging
                .MinimumLevel.Is(GetMinimumLogEventLevel(context))
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .WriteTo.Console())
            .UseCommandHandlers();
    }

    private static LogEventLevel GetMinimumLogEventLevel(HostBuilderContext context)
    {
        var verboseSpecified =
            context.Properties.TryGetValue(typeof(InvocationContext), out var value) &&
            value is InvocationContext invocationContext &&
            invocationContext.ParseResult.FindResultFor(GlobalOptions.Verbose) is not null;

        return verboseSpecified ? LogEventLevel.Debug : LogEventLevel.Fatal;
    }
}
