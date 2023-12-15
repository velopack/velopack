using Microsoft.Extensions.Hosting;
using Serilog.Events;
using Serilog;
using Microsoft.Extensions.Configuration;
using Squirrel.Csq.Commands;
using Microsoft.Extensions.DependencyInjection;
using Squirrel.Csq.Updates;
using Squirrel.Csq.Compat;

namespace Squirrel.Csq;

public class Program
{
    public static CliOption<string> TargetRuntime { get; }
        = new CliOption<string>("--runtime", "-r")
        .SetDescription("The target runtime to build packages for.")
        .SetArgumentHelpName("RID")
        .MustBeSupportedRid()
        .SetRequired();

    public static CliOption<bool> VerboseOption { get; }
        = new CliOption<bool>("--verbose")
        .SetDescription("Print diagnostic messages.");

    private static CliOption<FileSystemInfo> CsqSolutionPath { get; }
        = new CliOption<FileSystemInfo>("--solution")
        .SetDescription("Explicit path to project solution (.sln)")
        .AcceptExistingOnly();

    private static IServiceProvider Provider { get; set; }

    public static async Task<int> Main(string[] args)
    {
        CliRootCommand platformRootCommand = new CliRootCommand() {
            TargetRuntime,
            VerboseOption,
            CsqSolutionPath,
        };
        platformRootCommand.TreatUnmatchedTokensAsErrors = false;
        ParseResult parseResult = platformRootCommand.Parse(args);

        var runtime = RID.Parse(parseResult.GetValue(TargetRuntime) ?? SquirrelRuntimeInfo.SystemOs.GetOsShortName());
        var solutionPath = parseResult.GetValue(CsqSolutionPath);
        bool verbose = parseResult.GetValue(VerboseOption);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings {
            ApplicationName = "Clowd.Squirrel",
            EnvironmentName = "Production",
            ContentRootPath = Environment.CurrentDirectory,
            Configuration = new ConfigurationManager(),
        });

        builder.Services.AddSingleton<IRunnerFactory>(s => new RunnerFactory(s.GetRequiredService<Microsoft.Extensions.Logging.ILogger>(), solutionPath, s.GetRequiredService<IConfiguration>()));
        builder.Configuration.AddEnvironmentVariables("CSQ_");

        var minLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.Console()
            .CreateLogger();
        builder.Logging.AddSerilog();

        var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        Provider = host.Services;

        CliRootCommand rootCommand = new CliRootCommand($"Squirrel {SquirrelRuntimeInfo.SquirrelDisplayVersion} for creating and distributing Squirrel releases.") {
            TargetRuntime,
            VerboseOption,
            CsqSolutionPath,
        };

        switch (runtime.BaseRID) {
        case RuntimeOs.Windows:
            if (!SquirrelRuntimeInfo.IsWindows)
                logger.Warn("Cross-compiling will cause some commands and options of Squirrel to be unavailable.");
            Add(rootCommand, new PackWindowsCommand(), nameof(ICommandRunner.ExecutePackWindows));
            Add(rootCommand, new ReleasifyWindowsCommand(), nameof(ICommandRunner.ExecuteReleasifyWindows));
            break;
        case RuntimeOs.OSX:
            if (!SquirrelRuntimeInfo.IsOSX)
                throw new NotSupportedException("Cannot create OSX packages on non-OSX platforms.");
            Add(rootCommand, new BundleOsxCommand(), nameof(ICommandRunner.ExecuteBundleOsx));
            Add(rootCommand, new ReleasifyOsxCommand(), nameof(ICommandRunner.ExecuteReleasifyOsx));
            break;
        default:
            throw new NotSupportedException("Unsupported OS platform: " + runtime.BaseRID.GetOsLongName());
        }

        CliCommand downloadCommand = new CliCommand("download", "Download's the latest release from a remote update source.");
        Add(downloadCommand, new HttpDownloadCommand(), nameof(ICommandRunner.ExecuteHttpDownload));
        Add(downloadCommand, new S3DownloadCommand(), nameof(ICommandRunner.ExecuteS3Download));
        Add(downloadCommand, new GitHubDownloadCommand(), nameof(ICommandRunner.ExecuteGithubDownload));
        rootCommand.Add(downloadCommand);

        var uploadCommand = new CliCommand("upload", "Upload local package(s) to a remote update source.");
        Add(uploadCommand, new S3UploadCommand(), nameof(ICommandRunner.ExecuteS3Upload));
        Add(uploadCommand, new GitHubUploadCommand(), nameof(ICommandRunner.ExecuteGithubUpload));
        rootCommand.Add(uploadCommand);

        var cli = new CliConfiguration(rootCommand);
        return await cli.InvokeAsync(args);
    }

    private static CliCommand Add<T>(CliCommand parent, T command, string commandName)
        where T : BaseCommand
    {
        command.SetAction((ctx, token) => {
            command.SetProperties(ctx);
            command.TargetRuntime = RID.Parse(ctx.GetValue(TargetRuntime));
            var factory = Provider.GetRequiredService<IRunnerFactory>();
            return factory.CreateAndExecuteAsync(commandName, command);
        });
        parent.Subcommands.Add(command);
        return command;
    }
}
