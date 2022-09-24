using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Runtime.Versioning;
using Squirrel.CommandLine;
using Squirrel.SimpleSplat;

namespace Squirrel.Update
{
    [SupportedOSPlatform("osx")]
    class Program : IEnableLogger
    {
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Program));

        static AppDescOsx _app;
        static ILogger _logger;

        private static readonly Option<string> ProcessStartOption = new("--processStart", "Start an executable in the current version of the app package");
        private static readonly Option<string> ProcessStartAndWaitOption = new("--processStartAndWait", "Start an executable in the current version of the app package");
        private static readonly Option<bool> ForceLatestOption = new("--forceLatest", "Force updates the current version folder");
        private static readonly Option<string> ProcessStartArgsOption = new(new[] { "-a", "--processStartArgs" }, "Arguments that will be used when starting executable");
        
        public static int Main(string[] args)
        {
            RootCommand rootCommand = new() {
                ProcessStartOption,
                ProcessStartAndWaitOption,
                ForceLatestOption,
                ProcessStartArgsOption
            };
            rootCommand.SetHandler(Execute);
            rootCommand.AtLeastOneRequired(ProcessStartOption, ProcessStartAndWaitOption);
            return rootCommand.Invoke(args);
        }

        private static void Execute(InvocationContext context)
        {
            _app = new AppDescOsx();
            _logger = SetupLogLogger.RegisterLogger(_app.AppId);

            //Using the Diagram method here shows the structure of the CLI including any defaults
            Log.Info("Starting Squirrel Updater (OSX): " + context.ParseResult.Diagram());
            Log.Info("Updater location is: " + SquirrelRuntimeInfo.EntryExePath);


            string processStart = null;
            bool shouldWait = false;
            if (context.ParseResult.HasOption(ProcessStartArgsOption)) {
                processStart = context.ParseResult.GetValueForOption(ProcessStartOption);
            } else if (context.ParseResult.HasOption(ProcessStartAndWaitOption)) {
                processStart = context.ParseResult.GetValueForOption(ProcessStartAndWaitOption);
                shouldWait = true;
            }
            string processStartArgs = context.ParseResult.GetValueForOption(ProcessStartArgsOption);
            bool forceLatest = context.ParseResult.GetValueForOption(ForceLatestOption);
            try {
                
                ProcessStart(processStart, processStartArgs, shouldWait, forceLatest);

                Log.Info("Finished Squirrel Updater (OSX)");
            } catch (Exception ex) {
                context.Console.Error.WriteLine(ex.ToString());
                _logger?.Write(ex.ToString(), LogLevel.Fatal);
                context.ExitCode = -1;
            }
        }

        static void ProcessStart(string exeName, string arguments, bool shouldWait, bool forceLatest)
        {
            if (_app.CurrentlyInstalledVersion == null)
                throw new InvalidOperationException("ProcessStart is only valid in an installed application");

            if (shouldWait) PlatformUtil.WaitForParentProcessToExit();

            // todo https://stackoverflow.com/questions/51441576/how-to-run-app-as-sudo
            // https://stackoverflow.com/questions/10283062/getting-sudo-to-ask-for-password-via-the-gui
            // /usr/bin/osascript -e 'do shell script "/path/to/myscript args 2>&1 etc" with administrator privileges'

            var currentDir = _app.UpdateAndRetrieveCurrentFolder(forceLatest);

            var exe = "/usr/bin/open";
            var args = $"-n \"{currentDir}\" --args {arguments}";

            Log.Info($"Running: {exe} {args}");

            PlatformUtil.StartProcessNonBlocking(exe, args, null);
        }
    }
}