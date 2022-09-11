using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;

namespace Squirrel.CommandLine.Windows
{
    internal class PackCommand : Command, ICommand
    {
        string ICommand.HelpGroupName => "Package Authoring";

        public Option<DirectoryInfo> ReleaseDirectory { get; }
        //Question: Since these are already inside of the PackCommand should we drop the "PAck" prefix from the property names?
        public Option<string> PackId { get; }
        public Option<string> PackVersion { get; }
        public Option<string> PackTitle { get; }
        public Option<DirectoryInfo> PackDirectory { get; }
        public Option<string> PackAuthors { get; }
        public Option<FileInfo> ReleaseNotes { get; }
        public Option<bool> IncludePdb { get; }
        public Option<Bitness> BuildMsi { get; }
        public Option<string> SignParameters { get; }
        public Option<bool> SignSkipDll { get; }
        public Option<int> SignParallel { get; }
        public Option<string> SignTemplate { get; }
        public Option<bool> NoDelta { get; }
        public Option<string> Runtimes { get; }
        public Option<FileInfo> SplashImage { get; }
        public Option<FileInfo> Icon { get; }
        public Option<FileInfo> AppIcon { get; }
        public Option<string> SquirrelAwareExecutable { get; }

        public PackCommand()
            : base("pack", "Creates a Squirrel release from a folder containing application files")
        {
            ReleaseDirectory = new Option<DirectoryInfo>(new[] { "-r", "--releaseDir" }, "Output DIRECTORY for releasified packages") {
                ArgumentHelpName = "DIRECTORY"
            };
            Add(ReleaseDirectory);

            PackId = new Option<string>(new[] { "-u", "--packId" }, "Unique ID for release");
            Add(PackId);

            PackVersion = new Option<string>(new[] { "-v", "--packVersion" }, "Current VERSION for release") {
                ArgumentHelpName = "VERSION"
            };
            Add(PackVersion);

            PackDirectory = new Option<DirectoryInfo>(new[] { "-p", "--packDir" }, "DIRECTORY containing application files for release") {
                ArgumentHelpName = "DIRECTORY"
            };
            Add(PackDirectory);

            PackTitle = new Option<string>("--packTitle", "Optional display/friendly NAME for release") {
                ArgumentHelpName = "NAME"
            };
            Add(PackTitle);

            PackAuthors = new Option<string>("--packAuthors", "Optional company or list of release AUTHORS") {
                ArgumentHelpName = "AUTHORS"
            };
            Add(PackAuthors);

            ReleaseNotes = new Option<FileInfo>("--releaseNotes", "PATH to file with markdown notes for version") {
                ArgumentHelpName = "PATH"
            };
            Add(ReleaseNotes);

            SignParameters = new Option<string>(new[] { "-n", "--signParams" }, "Sign files via signtool.exe using these PARAMETERS") {
                ArgumentHelpName = "PARAMETERS"
            };
            Add(SignParameters);

            SignSkipDll = new Option<bool>("--signSkipDll", "Only signs EXE files, and skips signing DLL files.");
            Add(SignSkipDll);

            SignParallel = new Option<int>("--signParallel", () => SigningOptions.SignParallelDefault, "The number of files to sign in each call to signtool.exe") {
                ArgumentHelpName = "VALUE"
            };
            Add(SignParallel);

            SignTemplate = new Option<string>("--signTemplate", "Use a custom signing COMMAND. '{{file}}' will be replaced by the path of the file to sign.") {
                ArgumentHelpName = "COMMAND"
            };
            Add(SignTemplate);

            IncludePdb = new Option<bool>("--includePdb");
            Add(IncludePdb);

            NoDelta = new Option<bool>("--noDelta", "Skip the generation of delta packages");
            Add(NoDelta);

            Runtimes = new Option<string>(new[] { "-f", "--framework" }, "List of required RUNTIMES to install during setup. example: 'net6,vcredist143'") {
                ArgumentHelpName = "RUNTIMES"
            };
            Add(Runtimes);

            BuildMsi = new Option<Bitness>("--msi", "Compile a .msi machine-wide deployment tool with the specified BITNESS.");
            Add(BuildMsi);

            SplashImage = new Option<FileInfo>(new[] { "-s", "--splashImage" }, "PATH to image/gif displayed during installation") {
                ArgumentHelpName = "PATH"
            };
            Add(SplashImage);

            Icon = new Option<FileInfo>(new[] { "-i", "--icon" }, "PATH to .ico for Setup.exe and Update.exe");
            Add(Icon);

            AppIcon = new Option<FileInfo>("--appIcon", "PATH to .ico for 'Apps and Features' list");
            Add(AppIcon);

            SquirrelAwareExecutable = new Option<string>(new[] { "-e", "--mainExe" }, "NAME of one or more SquirrelAware executables") {
                ArgumentHelpName = "NAME"
            };
            Add(SquirrelAwareExecutable);

            this.SetHandler(Execute);
        }

        private void Execute(InvocationContext context)
        {
            Bitness msiBitness = context.ParseResult.GetValueForOption(BuildMsi);
            //TODO: Fix option's naming and types
            PackOptions packOptions = new PackOptions() {
                packId = context.ParseResult.GetValueForOption(PackId),
                packTitle = context.ParseResult.GetValueForOption(PackTitle),
                packVersion = context.ParseResult.GetValueForOption(PackVersion),
                packAuthors = context.ParseResult.GetValueForOption(PackAuthors),
                packDirectory = context.ParseResult.GetValueForOption(PackDirectory)?.FullName,
                includePdb = context.ParseResult.GetValueForOption(IncludePdb),
                releaseNotes = context.ParseResult.GetValueForOption(ReleaseNotes)?.FullName,

                //TODO: Hidden options
                //package = context.ParseResult.GetValueForOption(Pack)
                //baseUrl { get; set; }
                framework = context.ParseResult.GetValueForOption(Runtimes),
                splashImage = context.ParseResult.GetValueForOption(SplashImage)?.FullName,
                icon = context.ParseResult.GetValueForOption(Icon)?.FullName,
                appIcon = context.ParseResult.GetValueForOption(AppIcon)?.FullName,
                noDelta = context.ParseResult.GetValueForOption(NoDelta),
                msi = msiBitness == Bitness.Unknown ? null : msiBitness.ToString(),
                //debugSetupExe { get; set; }

                signParams = context.ParseResult.GetValueForOption(SignParameters),
                signTemplate = context.ParseResult.GetValueForOption(SignTemplate),
                signSkipDll = context.ParseResult.GetValueForOption(SignSkipDll),
                signParallel = context.ParseResult.GetValueForOption(SignParallel),

                releaseDir = context.ParseResult.GetValueForOption(ReleaseDirectory)?.FullName,
            };
            Commands.Pack(packOptions);
        }
    }
}