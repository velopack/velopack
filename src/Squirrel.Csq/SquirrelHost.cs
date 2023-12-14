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
        public static Option<string> TargetRuntime { get; }
            = new Option<string>(new[] { "-r", "--runtime" }, "The target runtime to build packages for.")
            .SetArgumentHelpName("RID")
            .MustBeSupportedRid()
            .SetRequired();

        public static Option<bool> VerboseOption { get; }
            = new Option<bool>("--verbose", "Print diagnostic messages.");

        public static Option<string[]> AddSearchPathOption { get; }
            = new Option<string[]>("--addSearchPath", "Add additional search directories when looking for helper exe's.")
            .SetArgumentHelpName("DIR");

        public static int Main(string[] args)
        {
            var logger = ConsoleLogger.RegisterLogger();

            RootCommand platformRootCommand = new RootCommand() {
                TargetRuntime,
                VerboseOption,
                AddSearchPathOption,
            };
            platformRootCommand.TreatUnmatchedTokensAsErrors = false;

            ParseResult parseResult = platformRootCommand.Parse(args);

            bool verbose = parseResult.GetValueForOption(VerboseOption);
            if (parseResult.GetValueForOption(AddSearchPathOption) is { } searchPath) {
                foreach (var v in searchPath) {
                    HelperFile.AddSearchPath(v);
                }
            }

            RootCommand rootCommand = new RootCommand($"Squirrel {SquirrelRuntimeInfo.SquirrelDisplayVersion} for creating and distributing Squirrel releases.");
            rootCommand.AddGlobalOption(TargetRuntime);
            rootCommand.AddGlobalOption(VerboseOption);
            rootCommand.AddGlobalOption(AddSearchPathOption);

            var runtime = RID.Parse(parseResult.GetValueForOption(TargetRuntime) ?? SquirrelRuntimeInfo.SystemOs.GetOsShortName());

            switch (runtime.BaseRID) {
            case RuntimeOs.Windows:
                if (!SquirrelRuntimeInfo.IsWindows)
                    logger.Write("Cross-compiling will cause some commands and options of Squirrel to be unavailable.", LogLevel.Warn);
                Add(rootCommand, new PackWindowsCommand(), Windows.Commands.Pack);
                Add(rootCommand, new ReleasifyWindowsCommand(), Windows.Commands.Releasify);

                // temporarily, upload only supports windows.
                Command uploadCommand = new Command("upload", "Upload local package(s) to a remote update source.");
                Add(uploadCommand, new S3UploadCommand(), options => S3Repository.UploadMissingPackages(options));
                Add(uploadCommand, new GitHubUploadCommand(), options => GitHubRepository.UploadMissingPackages(options));
                rootCommand.Add(uploadCommand);

                break;
            case RuntimeOs.OSX:
                if (!SquirrelRuntimeInfo.IsOSX)
                    logger.Write("Cross-compiling will cause some commands and options of Squirrel to be unavailable.", LogLevel.Warn);
                Add(rootCommand, new BundleOsxCommand(), OSX.Commands.Bundle);
                Add(rootCommand, new ReleasifyOsxCommand(), OSX.Commands.Releasify);
                break;
            default:
                throw new NotSupportedException("Unsupported OS platform: " + runtime.BaseRID.GetOsLongName());
            }

            if (verbose) {
                logger.Level = LogLevel.Debug;
            }

            Command downloadCommand = new Command("download", "Download's the latest release from a remote update source.");
            Add(downloadCommand, new HttpDownloadCommand(), options => SimpleWebRepository.DownloadRecentPackages(options));
            Add(downloadCommand, new S3DownloadCommand(), options => S3Repository.DownloadRecentPackages(options));
            Add(downloadCommand, new GitHubDownloadCommand(), options => GitHubRepository.DownloadRecentPackages(options));
            rootCommand.Add(downloadCommand);

            return rootCommand.Invoke(args);
        }

        private static Command Add<T>(Command parent, T command, Action<T> execute)
           where T : BaseCommand
        {
            command.SetHandler((ctx) => {
                command.SetProperties(ctx.ParseResult);
                command.TargetRuntime = RID.Parse(ctx.ParseResult.GetValueForOption(TargetRuntime));
                execute(command);
            });
            parent.AddCommand(command);
            return command;
        }

        private static Command Add<T>(Command parent, T command, Func<T, Task> execute)
          where T : BaseCommand
        {
            command.SetHandler((ctx) => {
                command.SetProperties(ctx.ParseResult);
                command.TargetRuntime = RID.Parse(ctx.ParseResult.GetValueForOption(TargetRuntime));
                return execute(command);
            });
            parent.AddCommand(command);
            return command;
        }
    }
}