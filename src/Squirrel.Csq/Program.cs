using Microsoft.Extensions.Hosting;
using Serilog.Events;
using Serilog;
using Microsoft.Extensions.Configuration;
using Squirrel.Csq.Commands;
using Squirrel.Deployment;
using Microsoft.Extensions.DependencyInjection;

namespace Squirrel.Csq;

public class Program
{
    public static CliOption<string> TargetRuntime { get; }
        = new CliOption<string>("runtime", "-r", "--runtime", "The target runtime to build packages for.")
        .SetArgumentHelpName("RID")
        .MustBeSupportedRid()
        .SetRequired();

    public static CliOption<bool> VerboseOption { get; }
        = new CliOption<bool>("--verbose", "Print diagnostic messages.");

    public static Task<int> Main(string[] args)
    {
        CliRootCommand platformRootCommand = new CliRootCommand() {
            TargetRuntime,
            VerboseOption,
        };
        platformRootCommand.TreatUnmatchedTokensAsErrors = false;
        ParseResult parseResult = platformRootCommand.Parse(args);

        var runtime = RID.Parse(parseResult.GetValue(TargetRuntime) ?? SquirrelRuntimeInfo.SystemOs.GetOsShortName());

        bool verbose = parseResult.GetValue(VerboseOption);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings {
            ApplicationName = "Clowd.Squirrel",
            EnvironmentName = "Production",
            ContentRootPath = Environment.CurrentDirectory,
            Configuration = new ConfigurationManager(),
        });

        var minLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.Console()
            .CreateLogger();

        builder.Logging.AddSerilog();

        var host = builder.Build();

        var logger = host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();

        CliRootCommand rootCommand = new CliRootCommand($"Squirrel {SquirrelRuntimeInfo.SquirrelDisplayVersion} for creating and distributing Squirrel releases.");
        rootCommand.Options.Add(TargetRuntime);
        rootCommand.Options.Add(VerboseOption);

        switch (runtime.BaseRID) {
        case RuntimeOs.Windows:
            if (!SquirrelRuntimeInfo.IsWindows)
                logger.Warn("Cross-compiling will cause some commands and options of Squirrel to be unavailable.");
            Add(rootCommand, new PackWindowsCommand(), Windows.Commands.Pack);
            Add(rootCommand, new ReleasifyWindowsCommand(), Windows.Commands.Releasify);
            break;
        case RuntimeOs.OSX:
            if (!SquirrelRuntimeInfo.IsOSX)
                throw new InvalidOperationException("Cannot create OSX packages on non-OSX platforms.");
            Add(rootCommand, new BundleOsxCommand(), OSX.Commands.Bundle);
            Add(rootCommand, new ReleasifyOsxCommand(), OSX.Commands.Releasify);
            break;
        default:
            throw new NotSupportedException("Unsupported OS platform: " + runtime.BaseRID.GetOsLongName());
        }

        CliCommand downloadCommand = new CliCommand("download", "Download's the latest release from a remote update source.");
        Add(downloadCommand, new HttpDownloadCommand(), options => SimpleWebRepository.DownloadRecentPackages(options));
        Add(downloadCommand, new S3DownloadCommand(), options => S3Repository.DownloadRecentPackages(options));
        Add(downloadCommand, new GitHubDownloadCommand(), options => GitHubRepository.DownloadRecentPackages(options));
        rootCommand.Add(downloadCommand);

        var uploadCommand = new CliCommand("upload", "Upload local package(s) to a remote update source.");
        Add(uploadCommand, new S3UploadCommand(), options => S3Repository.UploadMissingPackages(options));
        Add(uploadCommand, new GitHubUploadCommand(), options => GitHubRepository.UploadMissingPackages(options));
        rootCommand.Add(uploadCommand);

        var cli = new CliConfiguration(rootCommand);

        return cli.InvokeAsync(args);
    }

    private static CliCommand Add<T>(CliCommand parent, T command, Action<T> execute)
      where T : BaseCommand
    {
        command.SetAction((ctx) => {
            command.SetProperties(ctx);
            command.TargetRuntime = RID.Parse(ctx.GetValue(TargetRuntime));
            execute(command);
        });
        parent.Subcommands.Add(command);
        return command;
    }

    private static CliCommand Add<T>(CliCommand parent, T command, Func<T, Task> execute)
        where T : BaseCommand
    {
        command.SetAction((ctx, token) => {
            command.SetProperties(ctx);
            command.TargetRuntime = RID.Parse(ctx.GetValue(TargetRuntime));
            return execute(command);
        });
        parent.Subcommands.Add(command);
        return command;
    }
}
