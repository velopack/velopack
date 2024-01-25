using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Velopack.Packaging.Abstractions;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Logging;

namespace Velopack.Vpk;

public class Program
{
    public static CliOption<bool> VerboseOption { get; }
        = new CliOption<bool>("--verbose")
        .SetRecursive(true)
        .SetDescription("Print diagnostic messages.");

    public static CliOption<bool> LegacyConsole { get; }
        = new CliOption<bool>("--legacy-console")
        .SetRecursive(true)
        .SetDescription("Disable console colors and interactive components.");

    public static readonly string INTRO
        = $"Velopack CLI {VelopackRuntimeInfo.VelopackDisplayVersion} for creating and distributing releases.";

    public static async Task<int> Main(string[] args)
    {
        CliRootCommand platformRootCommand = new CliRootCommand() {
            VerboseOption,
            LegacyConsole,
        };
        platformRootCommand.TreatUnmatchedTokensAsErrors = false;
        ParseResult parseResult = platformRootCommand.Parse(args);
        bool verbose = parseResult.GetValue(VerboseOption);
        bool legacyConsole = parseResult.GetValue(LegacyConsole)
            || Console.IsOutputRedirected
            || Console.IsErrorRedirected;

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings {
            ApplicationName = "Velopack",
            EnvironmentName = "Production",
            ContentRootPath = Environment.CurrentDirectory,
            Configuration = new ConfigurationManager(),
        });

        builder.Configuration.AddEnvironmentVariables("VPK_");
        builder.Services.AddTransient(s => s.GetService<ILoggerFactory>().CreateLogger("vpk"));

        var conf = new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning);

        if (legacyConsole) {
            // spectre can have issues with redirected output, so we disable it.
            builder.Services.AddSingleton<IFancyConsole, BasicConsole>();
            conf.WriteTo.Console();
        } else {
            builder.Services.AddSingleton<IFancyConsole, SpectreConsole>();
            conf.WriteTo.Spectre();
        }

        Log.Logger = conf.CreateLogger();
        builder.Logging.AddSerilog();

        var host = builder.Build();
        var logger = host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger>();

        var rootCommand = new CliRootCommand(INTRO) {
            VerboseOption,
            LegacyConsole,
        };

        rootCommand.PopulateVelopackCommands(host.Services);

        var cli = new CliConfiguration(rootCommand);
        return await cli.InvokeAsync(args);
    }
}
