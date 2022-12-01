using System;
using System.CommandLine;
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
            = new Option<string[]>("--addSearchPath", "Add additional search directories when looking for helper exe's.")
            .SetArgumentHelpName("DIR");

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
                    logger.Write("Cross-compiling will cause some command and options of Squirrel to be unavailable.", LogLevel.Warn);
                rootCommand.AddCommandWithHandler(new PackWindowsCommand(), Windows.Commands.Pack);
                rootCommand.AddCommandWithHandler(new ReleasifyWindowsCommand(), Windows.Commands.Releasify);
                break;

            case "mac":
            case "osx":
            case "macos":
                if (!SquirrelRuntimeInfo.IsOSX)
                    logger.Write("Cross-compiling will cause some command and options of Squirrel to be unavailable.", LogLevel.Warn);
                rootCommand.AddCommandWithHandler(new BundleOsxCommand(), OSX.Commands.Bundle);
                rootCommand.AddCommandWithHandler(new ReleasifyOsxCommand(), OSX.Commands.Releasify);
                break;

            default:
                throw new NotSupportedException("Unsupported OS platform: " + xplat);
            }

            if (verbose) {
                logger.Level = LogLevel.Debug;
            }

            Command uploadCommand = new Command("upload", "Upload local package(s) to a remote update source.");
            uploadCommand.AddCommandWithHandler(new S3UploadCommand(), options => S3Repository.UploadMissingPackages(options));
            uploadCommand.AddCommandWithHandler(new GitHubUploadCommand(), options => GitHubRepository.UploadMissingPackages(options));

            Command downloadCommand = new Command("download", "Download's the latest release from a remote update source.");
            downloadCommand.AddCommandWithHandler(new HttpDownloadCommand(), options => SimpleWebRepository.DownloadRecentPackages(options));
            downloadCommand.AddCommandWithHandler(new S3DownloadCommand(), options => S3Repository.DownloadRecentPackages(options));
            downloadCommand.AddCommandWithHandler(new GitHubDownloadCommand(), options => GitHubRepository.DownloadRecentPackages(options));

            rootCommand.Add(uploadCommand);
            rootCommand.Add(downloadCommand);
            return rootCommand.Invoke(args);
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