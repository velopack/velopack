using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using Squirrel.CommandLine.Deployment;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine
{
    public class SquirrelHost
    {
        public static Option<string> PlatformOption { get; }
            = new Option<string>(new[] { "-x", "--xplat" }, "Select {PLATFORM} to cross-compile for (eg. win, osx)") {
                ArgumentHelpName = "PLATFORM"
            };
        public static Option<bool> VerboseOption { get; } = new Option<bool>("--verbose", "Print diagnostic messages.");

        public static int Main(string[] args)
        {
            var logger = ConsoleLogger.RegisterLogger();

            RootCommand platformRootCommand = new RootCommand() {
                PlatformOption,
                VerboseOption
            };
            platformRootCommand.TreatUnmatchedTokensAsErrors = false;

            ParseResult parseResult = platformRootCommand.Parse(args);
            
            string xplat = parseResult.GetValueForOption(PlatformOption) ?? SquirrelRuntimeInfo.SystemOsName;
            bool verbose = parseResult.GetValueForOption(VerboseOption);

            IEnumerable<Command> packageCommands;

            switch (xplat.ToLower()) {
            case "win":
            case "windows":
                if (!SquirrelRuntimeInfo.IsWindows)
                    logger.Write("Cross-compiling will cause some features of Squirrel to be disabled.", LogLevel.Warn);
                packageCommands = Windows.Commands.GetCommands();
                break;

            case "mac":
            case "osx":
            case "macos":
                if (!SquirrelRuntimeInfo.IsOSX)
                    logger.Write("Cross-compiling will cause some features of Squirrel to be disabled.", LogLevel.Warn);
                packageCommands = OSX.Commands.GetCommands();
                break;

            default:
                throw new NotSupportedException("Unsupported OS platform: " + xplat);
            }

            if (verbose) {
                logger.Level = LogLevel.Debug;
            }

            RootCommand rootCommand = new RootCommand($"Squirrel {SquirrelRuntimeInfo.SquirrelDisplayVersion} for creating and distributing Squirrel releases.");
            rootCommand.AddGlobalOption(PlatformOption);
            rootCommand.AddGlobalOption(VerboseOption);

            foreach (var command in packageCommands) {
                rootCommand.Add(command);
            }

            Command uploadCommand = new("upload", "Upload local package(s) to a remote update source.") {
                new S3UploadCommand(),
                new GitHubUploadCommand(),
            };

            Command downloadCommand = new("download", "Download's the latest release from a remote update source.") {
                new HttpDownloadCommand(),
                new S3DownloadCommand(),
                new GitHubDownloadCommand(),
            };

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
}