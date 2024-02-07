using System.ComponentModel;
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
using Velopack.Vpk.Converters;
using Velopack.Vpk.Logging;
using Velopack.Vpk.Updates;

namespace Velopack.Vpk;

public class Program
{
    public static CliOption<bool> VerboseOption { get; }
        = new CliOption<bool>("--verbose")
        .SetRecursive(true)
        .SetDescription("Print diagnostic messages.");

    public static CliOption<bool> LegacyConsoleOption { get; }
        = new CliOption<bool>("--legacyConsole", "-x")
        .SetRecursive(true)
        .SetDescription("Disable console colors and interactive components.");

    public static CliOption<bool> YesOption { get; }
        = new CliOption<bool>("--yes", "-y")
        .SetRecursive(true)
        .SetDescription("'yes' by instead of 'no' in non-interactive prompts.");

    public static readonly string INTRO
        = $"Velopack CLI {VelopackRuntimeInfo.VelopackDisplayVersion}, for distributing applications.";

    public static async Task<int> Main(string[] args)
    {
        CliCommand rootCommand = new CliCommand("vpk", INTRO) {
            new LongHelpCommand(),
            LegacyConsoleOption,
            YesOption,
            VerboseOption,
        };

        rootCommand.TreatUnmatchedTokensAsErrors = false;
        ParseResult parseResult = rootCommand.Parse(args);
        bool verbose = parseResult.GetValue(VerboseOption);
        bool legacyConsole = parseResult.GetValue(LegacyConsoleOption)
            || Console.IsOutputRedirected
            || Console.IsErrorRedirected;
        bool defaultYes = parseResult.GetValue(YesOption);
        rootCommand.TreatUnmatchedTokensAsErrors = true;

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings {
            ApplicationName = "Velopack",
            EnvironmentName = "Production",
            ContentRootPath = Environment.CurrentDirectory,
            Configuration = new ConfigurationManager(),
        });

        SetupConfig(builder);
        SetupLogging(builder, verbose, legacyConsole, defaultYes);

        var host = builder.Build();
        var provider = host.Services;

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
        downloadCommand.AddRepositoryDownload<LocalDownloadCommand, LocalRepository, LocalDownloadOptions>(provider);
        rootCommand.Add(downloadCommand);

        var uploadCommand = new CliCommand("upload", "Upload local package(s) to a remote update source.");
        uploadCommand.AddRepositoryUpload<GitHubUploadCommand, GitHubRepository, GitHubUploadOptions>(provider);
        uploadCommand.AddRepositoryUpload<S3UploadCommand, S3Repository, S3UploadOptions>(provider);
        uploadCommand.AddRepositoryUpload<LocalUploadCommand, LocalRepository, LocalUploadOptions>(provider);
        rootCommand.Add(uploadCommand);

        var deltaCommand = new CliCommand("delta", "Utilities for creating or applying delta packages.");
        deltaCommand.AddCommand<DeltaGenCommand, DeltaGenCommandRunner, DeltaGenOptions>(provider);
        deltaCommand.AddCommand<DeltaPatchCommand, DeltaPatchCommandRunner, DeltaPatchOptions>(provider);
        rootCommand.Add(deltaCommand);

        var cli = new CliConfiguration(rootCommand);
        return await cli.InvokeAsync(args);
    }

    private static void SetupConfig(IHostApplicationBuilder builder)
    {
        //builder.Configuration.AddJsonFile("vpk.json", optional: true);
        builder.Configuration.AddEnvironmentVariables("VPK_");
        TypeDescriptor.AddAttributes(typeof(FileInfo), new TypeConverterAttribute(typeof(FileInfoConverter)));
        TypeDescriptor.AddAttributes(typeof(DirectoryInfo), new TypeConverterAttribute(typeof(DirectoryInfoConverter)));
        builder.Services.AddTransient(s => s.GetService<ILoggerFactory>().CreateLogger("vpk"));
    }

    private static void SetupLogging(IHostApplicationBuilder builder, bool verbose, bool legacyConsole, bool defaultPromptValue)
    {
        var conf = new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning);

        builder.Services.AddSingleton(new DefaultPromptValueFactory(defaultPromptValue));

        if (legacyConsole) {
            // spectre can have issues with redirected output, so we disable it.
            builder.Services.AddSingleton<IFancyConsole, BasicConsole>();
            conf.WriteTo.ConsoleNoMarkup();
        } else {
            builder.Services.AddSingleton<IFancyConsole, SpectreConsole>();
            conf.WriteTo.Spectre();
        }

        Log.Logger = conf.CreateLogger();
        builder.Logging.AddSerilog();
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
            var config = provider.GetRequiredService<IConfiguration>();
            logger.LogInformation($"[bold]{Program.INTRO}[/]");
            var updateCheck = new UpdateChecker(logger);
            await updateCheck.CheckForUpdates();

            command.SetProperties(ctx, config);
            var options = OptionMapper.Map<TOpt>(command);

            try {
                await fn(options);
                // print the out of date warning again at the end as well.
                await updateCheck.CheckForUpdates();
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