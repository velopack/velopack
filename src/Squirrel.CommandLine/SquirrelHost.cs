using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Squirrel.CommandLine.Commands;
using Squirrel.CommandLine.Sync;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine
{
    public class SquirrelHost
    {
        public static Option<string> PlatformOption { get; }
            = new Option<string>(new[] { "-x", "--xplat" }, "Select {PLATFORM} to cross-compile for (eg. win, osx).") { ArgumentHelpName = "PLATFORM" };

        public static Option<bool> VerboseOption { get; }
            = new Option<bool>("--verbose", "Print diagnostic messages.");

        public static Option<string[]> AddSearchPathOption { get; }
            = new Option<string[]>("--addSearchPath", "Add additional search directories when looking for helper exe's.");

        public static int Main(string[] args)
        {
            var logger = ConsoleLogger.RegisterLogger();

            RootCommand platformRootCommand = new RootCommand() {
                PlatformOption,
                VerboseOption,
                AddSearchPathOption,
            };
            platformRootCommand.TreatUnmatchedTokensAsErrors = false;

            ParseResult parseResult = platformRootCommand.Parse(args);

            string xplat = parseResult.GetValueForOption(PlatformOption) ?? SquirrelRuntimeInfo.SystemOsName;
            bool verbose = parseResult.GetValueForOption(VerboseOption);
            if (parseResult.GetValueForOption(AddSearchPathOption) is { } searchPath) {
                foreach (var v in searchPath) {
                    HelperFile.AddSearchPath(v);
                }
            }

            RootCommand rootCommand = new RootCommand($"Squirrel {SquirrelRuntimeInfo.SquirrelDisplayVersion} for creating and distributing Squirrel releases.");
            rootCommand.AddGlobalOption(PlatformOption);
            rootCommand.AddGlobalOption(VerboseOption);
            rootCommand.AddGlobalOption(AddSearchPathOption);

            switch (xplat.ToLower()) {
            case "win":
            case "windows":
                if (!SquirrelRuntimeInfo.IsWindows)
                    logger.Write("Cross-compiling will cause some features of Squirrel to be disabled.", LogLevel.Warn);

                rootCommand.AddCommandWithHandler(new PackWindowsCommand(), Windows.Commands.Pack);
                rootCommand.AddCommandWithHandler(new ReleasifyWindowsCommand(), Windows.Commands.Releasify);
                break;

            case "mac":
            case "osx":
            case "macos":
                if (!SquirrelRuntimeInfo.IsOSX)
                    logger.Write("Cross-compiling will cause some features of Squirrel to be disabled.", LogLevel.Warn);

                rootCommand.AddCommandWithHandler(new PackOsxCommand(), OSX.Commands.Pack);
                break;

            default:
                throw new NotSupportedException("Unsupported OS platform: " + xplat);
            }

            if (verbose) {
                logger.Level = LogLevel.Debug;
            }

            Command uploadCommand = new Command("upload", "Upload local package(s) to a remote update source.")
                .AddCommandWithHandler(new S3UploadCommand(), options => new S3Repository(options).UploadMissingPackages())
                .AddCommandWithHandler(new GitHubUploadCommand(), options => new GitHubRepository(options).UploadMissingPackages());

            Command downloadCommand = new Command("download", "Download's the latest release from a remote update source.")
                .AddCommandWithHandler(new HttpDownloadCommand(), options => new SimpleWebRepository(options).DownloadPackages())
                .AddCommandWithHandler(new S3DownloadCommand(), options => new S3Repository(options).DownloadPackages())
                .AddCommandWithHandler(new GitHubDownloadCommand(), options => new GitHubRepository(options).DownloadPackages());

            rootCommand.Add(uploadCommand);
            rootCommand.Add(downloadCommand);

            var builder = new CommandLineBuilder(rootCommand)
                //This is to work around an issue with UseHelp(80) not properly overriding the max width
                //https://github.com/dotnet/command-line-api/pull/1864
                .UseHelpBuilder(context => new HelpBuilder(context.ParseResult.Parser.Configuration.LocalizationResources, 80))
                .UseDefaults();
            var parser = builder.Build();
            return parser.Invoke(args);
        }
    }

    public static class SquirrelHostExtensions
    {
        public static Command AddCommandWithHandler<T>(this Command root, T command, Action<T> execute)
            where T : BaseCommand
        {
            command.SetHandler((ctx) => {
                command.SetProperties(ctx);
                execute(command);
            });
            root.AddCommand(command);
            return command;
        }

        public static Command AddCommandWithHandler<T>(this Command root, T command, Func<T, Task> execute)
          where T : BaseCommand
        {
            command.SetHandler((ctx) => {
                command.SetProperties(ctx);
                return execute(command);
            });
            root.AddCommand(command);
            return command;
        }
    }
}