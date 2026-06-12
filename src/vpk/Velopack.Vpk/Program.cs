using System.ComponentModel;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Core.Json;
using Velopack.Core.Validation;
using Velopack.Deployment;
using Velopack.Flow;
using Velopack.Flow.Commands;
using Velopack.Packaging.Commands;
using Velopack.Packaging.Exceptions;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Commands.Deployment;
using Velopack.Vpk.Commands.Flow;
using Velopack.Vpk.Commands.Packaging;
using Velopack.Vpk.Converters;
using Velopack.Vpk.Logging;
using Velopack.Vpk.Updates;

namespace Velopack.Vpk;

public class Program
{
    public static Option<bool> VerboseOption { get; }
        = new Option<bool>("--verbose")
        .SetRecursive(true)
        .SetDescription("Print diagnostic messages.");

    public static Option<bool> LegacyConsoleOption { get; }
        = new Option<bool>("--legacyConsole", "-x")
        .SetRecursive(true)
        .SetDescription("Disable console colors and interactive components.");

    public static Option<bool> YesOption { get; }
        = new Option<bool>("--yes", "-y")
        .SetRecursive(true)
        .SetDescription("'yes' by instead of 'no' in non-interactive prompts.");

    public static Option<bool> SkipUpdatesOption { get; }
        = new Option<bool>("--skip-updates")
        .SetRecursive(true)
        .SetDescription("Skip update checks");

    public static Directive WindowsDirective { get; } = new Directive("win") {
        Description = "Show and run Windows specific commands."
    };

    public static Directive LinuxDirective { get; } = new Directive("linux") {
        Description = "Show and run Linux specific commands."
    };

    public static Directive OsxDirective { get; } = new Directive("osx") {
        Description = "Show and run MacOS specific commands."
    };

    public static Directive JsonDirective { get; } = new Directive("json") {
        Description = "Read command options from a JSON config file (eg. 'vpk [json] pack myconfig.json')."
    };

    public static readonly string INTRO
        = $"Velopack CLI {VelopackRuntimeInfo.VelopackDisplayVersion}, for distributing applications.";

    public static async Task<int> Main(string[] args)
    {
        RootCommand rootCommand = new RootCommand(INTRO);
        rootCommand.Options.Clear(); // remove the default help option
        rootCommand.Options.Add(new LongHelpCommand());
        rootCommand.Options.Add(LegacyConsoleOption);
        rootCommand.Options.Add(YesOption);
        rootCommand.Options.Add(VerboseOption);
        rootCommand.Options.Add(SkipUpdatesOption);
        rootCommand.Directives.Add(WindowsDirective);
        rootCommand.Directives.Add(LinuxDirective);
        rootCommand.Directives.Add(OsxDirective);
        rootCommand.Directives.Add(JsonDirective);

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
            rootCommand.AddCommand<OsxBundleCommand, OsxBundleCommandRunner, OsxBundleOptions>(provider);
            if (VelopackRuntimeInfo.IsOSX) {
                rootCommand.AddCommand<OsxPackCommand, OsxPackCommandRunner, OsxPackOptions>(provider);
            }
            break;
        default:
            throw new NotSupportedException("Unsupported OS platform: " + VelopackRuntimeInfo.SystemOs.GetOsLongName());
        }

        var downloadCommand = new Command("download", "Download's the latest release from a remote update source.");
        downloadCommand.AddCommand<GitHubDownloadCommand, GitHubDownloadCommandRunner, GitHubDownloadOptions>(provider);
        downloadCommand.AddCommand<GiteaDownloadCommand, GiteaDownloadCommandRunner, GiteaDownloadOptions>(provider);
        downloadCommand.AddCommand<S3DownloadCommand, S3DownloadCommandRunner, S3DownloadOptions>(provider);
        downloadCommand.AddCommand<AzureDownloadCommand, AzureDownloadCommandRunner, AzureDownloadOptions>(provider);
        downloadCommand.AddCommand<LocalDownloadCommand, LocalDownloadCommandRunner, LocalDownloadOptions>(provider);
        downloadCommand.AddCommand<HttpDownloadCommand, HttpDownloadCommandRunner, HttpDownloadOptions>(provider);
        rootCommand.Add(downloadCommand);

        var uploadCommand = new Command("upload", "Upload local package(s) to a remote update source.");
        uploadCommand.AddCommand<GitHubUploadCommand, GitHubUploadCommandRunner, GitHubUploadOptions>(provider);
        uploadCommand.AddCommand<GiteaUploadCommand, GiteaUploadCommandRunner, GiteaUploadOptions>(provider);
        uploadCommand.AddCommand<S3UploadCommand, S3UploadCommandRunner, S3UploadOptions>(provider);
        uploadCommand.AddCommand<AzureUploadCommand, AzureUploadCommandRunner, AzureUploadOptions>(provider);
        uploadCommand.AddCommand<LocalUploadCommand, LocalUploadCommandRunner, LocalUploadOptions>(provider);
        rootCommand.Add(uploadCommand);

        var deltaCommand = new Command("delta", "Utilities for creating or applying delta packages.");
        deltaCommand.AddCommand<DeltaGenCommand, DeltaGenCommandRunner, DeltaGenOptions>(provider);
        deltaCommand.AddCommand<DeltaPatchCommand, DeltaPatchCommandRunner, DeltaPatchOptions>(provider);
        rootCommand.Add(deltaCommand);

        HideCommand(rootCommand.AddCommand<LoginCommand, LoginCommandRunner, LoginOptions>(provider));
        HideCommand(rootCommand.AddCommand<LogoutCommand, LogoutCommandRunner, LogoutOptions>(provider));
        HideCommand(rootCommand.AddCommand<PublishCommand, PublishCommandRunner, PublishOptions>(provider));

        var flowCommand = new Command("flow", "Commands for interacting with Velopack Flow.") { Hidden = true };
        HideCommand(flowCommand.AddCommand<ApiCommand, ApiCommandRunner, ApiOptions>(provider));
        rootCommand.Add(flowCommand); 

        return await rootCommand.Parse(args).InvokeAsync();

        static void HideCommand(Command command) => command.Hidden = true;
    }

    private static void SetupConfig(HostApplicationBuilder builder)
    {
        //builder.Configuration.AddJsonFile("vpk.json", optional: true);
        builder.Configuration.AddEnvironmentVariables("VPK_");
        TypeDescriptor.AddAttributes(typeof(FileInfo), new TypeConverterAttribute(typeof(FileInfoConverter)));
        TypeDescriptor.AddAttributes(typeof(DirectoryInfo), new TypeConverterAttribute(typeof(DirectoryInfoConverter)));
        TypeDescriptor.AddAttributes(typeof(FileSystemInfo), new TypeConverterAttribute(typeof(FileSystemInfoConverter)));
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
}

public static class ProgramCommandExtensions
{
    public static Command AddCommand<TCli, TCmd, TOpt>(this Command parent, IServiceProvider provider)
        where TCli : BaseCommand, new()
        where TCmd : ValidatedCommand<TOpt>
        where TOpt : class, new()
    {
        var runner = ActivatorUtilities.CreateInstance<TCmd>(provider);
        return parent.Add<TCli, TOpt>(provider, runner.Validator, runner.Run);
    }

    private static Command Add<TCli, TOpt>(this Command parent, IServiceProvider provider, IValidator<TOpt> validator, Func<TOpt, Task> fn)
        where TCli : BaseCommand, new()
        where TOpt : class, new()
    {
        var command = new TCli();

        // mark options as required in the help text when the runner's validator
        // has a NotNull/NotEmpty rule for the property the option maps to.
        if (validator != null) {
            command.ApplyRequiredHints(validator.GetRequiredProperties());
        }
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

            try {
                bool jsonMode = ctx.GetResult(Program.JsonDirective) != null;
                string jsonFile = ctx.GetValue(command.JsonConfigArgument);

                if (jsonMode) {
                    if (jsonFile == null) {
                        throw new UserInfoException(
                            $"The [json] directive requires a path to a JSON config file. Eg. 'vpk [json] {command.Name} myconfig.json'.");
                    }

                    var explicitOptions = command.GetExplicitOptionNames(ctx);
                    if (explicitOptions.Any()) {
                        throw new UserInfoException(
                            $"When using the [json] directive, all options must be provided in the JSON file. " +
                            $"The following command line options are not allowed: {string.Join(", ", explicitOptions)}.");
                    }
                } else if (jsonFile != null) {
                    throw new UserInfoException(
                        $"Unexpected argument '{jsonFile}'. Did you mean 'vpk [json] {command.Name} {jsonFile}'?");
                }

                // hydrate the command from env vars and cli values/defaults, and map to options.
                command.SetProperties(ctx, config, defaults.TargetOs);
                var options = OptionMapper.Map<TOpt>(command);

                if (jsonMode) {
                    // overlay the json config on top, so precedence is json > env vars > defaults.
                    JsonConfigLoader.Populate(jsonFile, options);
                }

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