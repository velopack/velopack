using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Velopack.Deployment;
using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Commands;
using Velopack.Packaging.Exceptions;
using Velopack.Packaging.Flow;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Commands.Deployment;
using Velopack.Vpk.Commands.Flow;
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

    public static CliOption<bool> SkipUpdatesOption { get; }
        = new CliOption<bool>("--skip-updates")
        .SetRecursive(true)
        .SetDescription("Skip update checks");

    public static CliDirective WindowsDirective { get; } = new CliDirective("win") {
        Description = "Show and run Windows specific commands."
    };

    public static CliDirective LinuxDirective { get; } = new CliDirective("linux") {
        Description = "Show and run Linux specific commands."
    };

    public static CliDirective OsxDirective { get; } = new CliDirective("osx") {
        Description = "Show and run MacOS specific commands."
    };

    public static readonly string INTRO
        = $"Velopack CLI {VelopackRuntimeInfo.VelopackDisplayVersion}, for distributing applications.";

    public static async Task<int> Main(string[] args)
    {
        CliRootCommand rootCommand = new CliRootCommand(INTRO);
        rootCommand.Options.Clear(); // remove the default help option
        rootCommand.Options.Add(new LongHelpCommand());
        rootCommand.Options.Add(LegacyConsoleOption);
        rootCommand.Options.Add(YesOption);
        rootCommand.Options.Add(VerboseOption);
        rootCommand.Options.Add(SkipUpdatesOption);
        rootCommand.Directives.Add(WindowsDirective);
        rootCommand.Directives.Add(LinuxDirective);
        rootCommand.Directives.Add(OsxDirective);

        rootCommand.TreatUnmatchedTokensAsErrors = false;
        ParseResult parseResult = rootCommand.Parse(args);
        bool verbose = parseResult.GetValue(VerboseOption);
        bool legacyConsole = parseResult.GetValue(LegacyConsoleOption)
            || Console.IsOutputRedirected
            || Console.IsErrorRedirected;
        bool defaultYes = parseResult.GetValue(YesOption);
        bool directiveWin = parseResult.GetResult(WindowsDirective) != null;
        bool directiveLinux = parseResult.GetResult(LinuxDirective) != null;
        bool directiveOsx = parseResult.GetResult(OsxDirective) != null;
        bool skipUpdates = parseResult.GetValue(SkipUpdatesOption);
        rootCommand.TreatUnmatchedTokensAsErrors = true;

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings {
            ApplicationName = "Velopack",
            EnvironmentName = "Production",
            ContentRootPath = Environment.CurrentDirectory,
            Configuration = new ConfigurationManager(),
        });

        SetupConfig(builder);
        SetupLogging(builder, verbose, legacyConsole);
        SetupVelopackService(builder.Services);

        RuntimeOs targetOs = VelopackRuntimeInfo.SystemOs;
        if (new bool[] { directiveWin, directiveLinux, directiveOsx }.Count(x => x) > 1) {
            throw new UserInfoException(
                "Invalid arguments: Only one OS directive can be specified at a time: either [win], [linux], or [osx].");
        }

        if (directiveWin) {
            targetOs = RuntimeOs.Windows;
        } else if (directiveLinux) {
            targetOs = RuntimeOs.Linux;
        } else if (directiveOsx) {
            targetOs = RuntimeOs.OSX;
        }

        builder.Services.AddSingleton(new VelopackDefaults(defaultYes, targetOs, skipUpdates));

        var host = builder.Build();
        var provider = host.Services;
        var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger>();

        if (targetOs != VelopackRuntimeInfo.SystemOs) {
            logger.LogInformation($"Directive enabled for cross-compiling from {VelopackRuntimeInfo.SystemOs} (current os) to {targetOs}.");
        }

        switch (targetOs) {
        case RuntimeOs.Windows:
            rootCommand.AddCommand<WindowsPackCommand, WindowsPackCommandRunner, WindowsPackOptions>(provider);
            break;
        case RuntimeOs.Linux:
            rootCommand.AddCommand<LinuxPackCommand, LinuxPackCommandRunner, LinuxPackOptions>(provider);
            break;
        case RuntimeOs.OSX:
            if (VelopackRuntimeInfo.IsOSX) {
                rootCommand.AddCommand<OsxBundleCommand, OsxBundleCommandRunner, OsxBundleOptions>(provider);
                rootCommand.AddCommand<OsxPackCommand, OsxPackCommandRunner, OsxPackOptions>(provider);
            } else {
                throw new NotSupportedException($"Cross-compiling from {VelopackRuntimeInfo.SystemOs} to MacOS is not supported.");
            }
            break;
        default:
            throw new NotSupportedException("Unsupported OS platform: " + VelopackRuntimeInfo.SystemOs.GetOsLongName());
        }

        var downloadCommand = new CliCommand("download", "Download's the latest release from a remote update source.");
        downloadCommand.AddRepositoryDownload<GitHubDownloadCommand, GitHubRepository, GitHubDownloadOptions>(provider);
        downloadCommand.AddRepositoryDownload<GiteaDownloadCommand, GiteaRepository, GiteaDownloadOptions>(provider);
        downloadCommand.AddRepositoryDownload<S3DownloadCommand, S3Repository, S3DownloadOptions>(provider);
        downloadCommand.AddRepositoryDownload<AzureDownloadCommand, AzureRepository, AzureDownloadOptions>(provider);
        downloadCommand.AddRepositoryDownload<LocalDownloadCommand, LocalRepository, LocalDownloadOptions>(provider);
        downloadCommand.AddRepositoryDownload<HttpDownloadCommand, HttpRepository, HttpDownloadOptions>(provider);
        rootCommand.Add(downloadCommand);

        var uploadCommand = new CliCommand("upload", "Upload local package(s) to a remote update source.");
        uploadCommand.AddRepositoryUpload<GitHubUploadCommand, GitHubRepository, GitHubUploadOptions>(provider);
        uploadCommand.AddRepositoryUpload<GiteaUploadCommand, GiteaRepository, GiteaUploadOptions>(provider);
        uploadCommand.AddRepositoryUpload<S3UploadCommand, S3Repository, S3UploadOptions>(provider);
        uploadCommand.AddRepositoryUpload<AzureUploadCommand, AzureRepository, AzureUploadOptions>(provider);
        uploadCommand.AddRepositoryUpload<LocalUploadCommand, LocalRepository, LocalUploadOptions>(provider);
        rootCommand.Add(uploadCommand);

        var deltaCommand = new CliCommand("delta", "Utilities for creating or applying delta packages.");
        deltaCommand.AddCommand<DeltaGenCommand, DeltaGenCommandRunner, DeltaGenOptions>(provider);
        deltaCommand.AddCommand<DeltaPatchCommand, DeltaPatchCommandRunner, DeltaPatchOptions>(provider);
        rootCommand.Add(deltaCommand);

        HideCommand(rootCommand.AddCommand<LoginCommand, LoginCommandRunner, LoginOptions>(provider));
        HideCommand(rootCommand.AddCommand<LogoutCommand, LogoutCommandRunner, LogoutOptions>(provider));
        HideCommand(rootCommand.AddCommand<PublishCommand, PublishCommandRunner, PublishOptions>(provider));

        var flowCommand = new CliCommand("flow", "Commands for interacting with Velopack Flow.") { Hidden = true };
        HideCommand(flowCommand.AddCommand<ApiCommand, ApiCommandRunner, ApiOptions>(provider));
        rootCommand.Add(flowCommand); 

        var cli = new CliConfiguration(rootCommand);
        return await cli.InvokeAsync(args); 

        static void HideCommand(CliCommand command) => command.Hidden = true;
    }

    private static void SetupConfig(HostApplicationBuilder builder)
    {
        //builder.Configuration.AddJsonFile("vpk.json", optional: true);
        builder.Configuration.AddEnvironmentVariables("VPK_");
        TypeDescriptor.AddAttributes(typeof(FileInfo), new TypeConverterAttribute(typeof(FileInfoConverter)));
        TypeDescriptor.AddAttributes(typeof(DirectoryInfo), new TypeConverterAttribute(typeof(DirectoryInfoConverter)));
        builder.Services.AddTransient(s => s.GetService<ILoggerFactory>().CreateLogger("vpk"));
    }

    private static void SetupLogging(HostApplicationBuilder builder, bool verbose, bool legacyConsole)
    {
        var levelSwitch = new LoggingLevelSwitch {
            MinimumLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information
        };
        var conf = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning);
        
        if (legacyConsole) {
            // spectre can have issues with redirected output, so we disable it.
            builder.Services.AddSingleton<IFancyConsole, BasicConsole>();
            conf.WriteTo.ConsoleNoMarkup();
        } else {
            builder.Services.AddSingleton<IFancyConsole, SpectreConsole>();
            conf.WriteTo.Spectre();
        }
        builder.Services.AddSingleton(levelSwitch);
        builder.Services.AddSingleton<IConsole>(sp => sp.GetRequiredService<IFancyConsole>());

        Log.Logger = conf.CreateLogger();
        builder.Logging.AddSerilog();
    }

    private static void SetupVelopackService(IServiceCollection services)
    {
        services.AddSingleton<IVelopackFlowServiceClient, VelopackFlowServiceClient>();
        services.AddSingleton<HmacAuthHttpClientHandler>();
        services.AddHttpClient().ConfigureHttpClientDefaults(x => 
            x.AddHttpMessageHandler<HmacAuthHttpClientHandler>()
                .ConfigureHttpClient(httpClient => httpClient.Timeout = TimeSpan.FromMinutes(60)));
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
            var console = provider.GetRequiredService<IFancyConsole>();
            var config = provider.GetRequiredService<IConfiguration>();
            var defaults = provider.GetRequiredService<VelopackDefaults>();
            var logLevelSwitch = provider.GetRequiredService<LoggingLevelSwitch>();

            command.Initialize(logLevelSwitch);

            logger.LogInformation($"[bold]{Program.INTRO}[/]");
            var updateCheck = new UpdateChecker(logger, defaults);
            await updateCheck.CheckForUpdates();

            command.SetProperties(ctx, config, defaults.TargetOs);
            var options = OptionMapper.Map<TOpt>(command);

            try {
                await fn(options);
                // print the out of date warning again at the end as well.
                await updateCheck.CheckForUpdates();
                return 0;
            } catch (Exception ex) when (ex is ProcessFailedException or UserInfoException) {
                // some exceptions are just user info / user error, so don't need a stack trace.
                logger.Fatal($"[bold orange3]{console.EscapeMarkup(ex.Message)}[/]");
                return -1;
            } catch (Exception ex) {
                logger.Fatal(ex);
                return -1;
            }
        });
        parent.Subcommands.Add(command);
        return command;
    }
}