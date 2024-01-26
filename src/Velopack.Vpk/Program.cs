using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Velopack.Deployment;
using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Commands;
using Velopack.Packaging.Exceptions;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Logging;
using Velopack.Vpk.Updates;

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
        = $"Velopack CLI {VelopackRuntimeInfo.VelopackDisplayVersion}, for distributing applications.";

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
        var provider = host.Services;

        var rootCommand = new CliRootCommand(INTRO) {
            VerboseOption,
            LegacyConsole,
        };

        if (VelopackRuntimeInfo.IsWindows) {
            rootCommand.AddCommand<WindowsPackCommand, WindowsPackCommandRunner, WindowsPackOptions>(provider);
        } else if (VelopackRuntimeInfo.IsOSX) {
            rootCommand.AddCommand<OsxBundleCommand, OsxBundleCommandRunner, OsxBundleOptions>(provider);
            rootCommand.AddCommand<OsxPackCommand, OsxPackCommandRunner, OsxPackOptions>(provider);
        } else if (VelopackRuntimeInfo.IsLinux) {
            rootCommand.AddCommand<LinuxPackCommand, LinuxPackCommandRunner, LinuxPackOptions>(provider);
        } else {
            throw new NotSupportedException("Unsupported OS platform: " + VelopackRuntimeInfo.SystemOs.GetOsLongName());
        }

        var downloadCommand = new CliCommand("download", "Download's the latest release from a remote update source.");
        downloadCommand.AddRepositoryDownload<GitHubDownloadCommand, GitHubRepository, GitHubDownloadOptions>(provider);
        downloadCommand.AddRepositoryDownload<S3DownloadCommand, S3Repository, S3DownloadOptions>(provider);
        downloadCommand.AddRepositoryDownload<HttpDownloadCommand, HttpRepository, HttpDownloadOptions>(provider);
        rootCommand.Add(downloadCommand);

        var uploadCommand = new CliCommand("upload", "Upload local package(s) to a remote update source.");
        uploadCommand.AddRepositoryUpload<GitHubUploadCommand, GitHubRepository, GitHubUploadOptions>(provider);
        uploadCommand.AddRepositoryUpload<S3UploadCommand, S3Repository, S3UploadOptions>(provider);
        rootCommand.Add(uploadCommand);

        var deltaCommand = new CliCommand("delta", "Utilities for creating or applying delta packages.");
        deltaCommand.AddCommand<DeltaGenCommand, DeltaGenCommandRunner, DeltaGenOptions>(provider);
        deltaCommand.AddCommand<DeltaPatchCommand, DeltaPatchCommandRunner, DeltaPatchOptions>(provider);
        rootCommand.Add(deltaCommand);

        var cli = new CliConfiguration(rootCommand);
        return await cli.InvokeAsync(args);
    }
}

public static class ProgramCommandExtensions
{
    public static CliCommand AddCommand<TCli, TCmd, TOpt>(this CliCommand parent, IServiceProvider provider)
        where TCli : BaseCommand, new()
        where TCmd : ICommand<TOpt>
        where TOpt : class, new()
    {
        return parent.Add<TCli, TOpt>(provider, (options) => {
            var runner = ActivatorUtilities.CreateInstance<TCmd>(provider);
            return runner.Run(options);
        });
    }

    public static CliCommand AddRepositoryDownload<TCli, TCmd, TOpt>(this CliCommand parent, IServiceProvider provider)
        where TCli : BaseCommand, new()
        where TCmd : IRepositoryCanDownload<TOpt>
        where TOpt : RepositoryOptions, new()
    {
        return parent.Add<TCli, TOpt>(provider, (options) => {
            var runner = ActivatorUtilities.CreateInstance<TCmd>(provider);
            return runner.DownloadLatestFullPackageAsync(options);
        });
    }

    public static CliCommand AddRepositoryUpload<TCli, TCmd, TOpt>(this CliCommand parent, IServiceProvider provider)
        where TCli : BaseCommand, new()
        where TCmd : IRepositoryCanUpload<TOpt>
        where TOpt : RepositoryOptions, new()
    {
        return parent.Add<TCli, TOpt>(provider, (options) => {
            var runner = ActivatorUtilities.CreateInstance<TCmd>(provider);
            return runner.UploadMissingAssetsAsync(options);
        });
    }

    private static CliCommand Add<TCli, TOpt>(this CliCommand parent, IServiceProvider provider, Func<TOpt, Task> fn)
        where TCli : BaseCommand, new()
        where TOpt : class, new()
    {
        var command = new TCli();
        command.SetAction(async (ctx, token) => {
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger>();
            logger.LogInformation($"[bold]{Program.INTRO}[/]");
            var updateCheck = new UpdateChecker(logger);
            await updateCheck.CheckForUpdates();

            command.SetProperties(ctx);
            var options = OptionMapper.Map<TOpt>(command);

            try {
                await fn(options);
                return 0;
            } catch (Exception ex) when (ex is ProcessFailedException or UserInfoException) {
                // some exceptions are just user info / user error, so don't need a stack trace.
                logger.Fatal($"[bold orange3]{ex.Message}[/]");
                return -1;
            } catch (Exception ex) {
                logger.Fatal(ex, $"Command {typeof(TCli).Name} had an exception.");
                return -1;
            }
        });
        parent.Subcommands.Add(command);
        return command;
    }
}