using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;

namespace Squirrel.CommandLine.Commands
{
    public class SigningCommand : BaseCommand
    {
        public string SignParameters { get; private set; }

        public bool SignSkipDll { get; private set; }

        public int SignParallel { get; private set; }

        public string SignTemplate { get; private set; }

        protected SigningCommand(string name, string description)
            : base(name, description)
        {
            var signTemplate = AddOption<string>("--signTemplate", (v) => SignTemplate = v)
                .SetDescription("Use a custom signing command. '{{file}}' will be replaced by the path of the file to sign.")
                .SetArgumentHelpName("COMMAND")
                .MustContain("{{file}}");

            if (SquirrelRuntimeInfo.IsWindows) {
                var signParams = AddOption<string>(new[] { "--signParams", "-n" }, (v) => SignParameters = v)
                    .SetDescription("Sign files via signtool.exe using these parameters.")
                    .SetArgumentHelpName("PARAMS");

                this.AreMutuallyExclusive(signTemplate, signParams);

                AddOption<bool>("--signSkipDll", (v) => SignSkipDll = v)
                    .SetDescription("Only signs EXE files, and skips signing DLL files.");

                AddOption<int>("--signParallel", (v) => SignParallel = v)
                    .SetDescription("The number of files to sign in each call to signtool.exe.")
                    .SetArgumentHelpName("NUM")
                    .MustBeBetween(1, 1000)
                    .SetDefaultValue(10);
            }
        }
    }

    public class ReleasifyWindowsCommand : SigningCommand
    {
        public FileInfo Package { get; set; }

        public string BaseUrl { get; private set; }

        public FileInfo DebugSetupExe { get; private set; }

        public bool NoDelta { get; private set; }

        public string Runtimes { get; private set; }

        public FileInfo SplashImage { get; private set; }

        public FileInfo Icon { get; private set; }

        public string[] SquirrelAwareExecutableNames { get; private set; }

        public FileInfo AppIcon { get; private set; }

        public string BuildMsi { get; private set; }

        public ReleasifyWindowsCommand()
            : this("releasify", "Take an existing nuget package and convert it into a Squirrel release.")
        {
            AddOption<FileInfo>(new[] { "-p", "--package" }, (v) => Package = v)
                .SetDescription("Path to a '.nupkg' package to releasify.")
                .SetArgumentHelpName("PATH")
                .SetRequired()
                .ExistingOnly()
                .RequiresExtension(".nupkg");
        }

        /// <summary>
        /// This constructor is used by the pack command, which requires all the same properties but 
        /// does not allow the user to provide the Package (it is created/populated by Squirrel).
        /// </summary>
        protected ReleasifyWindowsCommand(string name, string description)
            : base(name, description)
        {
            AddOption<Uri>(new[] { "-b", "--baseUrl" }, (v) => BaseUrl = v?.AbsoluteUri)
                .SetDescription("Provides a base URL to prefix the RELEASES file packages with.")
                .SetHidden()
                .MustBeValidHttpUri();

            AddOption<FileInfo>("--debugSetupExe", (v) => DebugSetupExe = v)
                .SetDescription("Uses the Setup.exe at this {PATH} to create the bundle, and then replaces it with the bundle. " +
                                "Used for locally debugging Setup.exe with a real bundle attached.")
                .SetArgumentHelpName("PATH")
                .SetHidden()
                .ExistingOnly()
                .RequiresExtension(".exe");

            AddOption<bool>("--noDelta", (v) => NoDelta = v)
                .SetDescription("Skip the generation of delta packages.");

            var framework = AddOption<string>(new[] { "-f", "--framework" }, (v) => Runtimes = v)
                .SetDescription("List of required runtimes to install during setup. example: 'net6,vcredist143'.")
                .SetArgumentHelpName("RUNTIMES");

            framework.AddValidator(MustBeValidFrameworkString);

            AddOption<FileInfo>(new[] { "-s", "--splashImage" }, (v) => SplashImage = v)
                .SetDescription("Path to image displayed during installation.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<FileInfo>(new[] { "-i", "--icon" }, (v) => Icon = v)
                .SetDescription("Path to .ico for Setup.exe and Update.exe.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly()
                .RequiresExtension(".ico");

            AddOption<string[]>(new[] { "-e", "--mainExe" }, (v) => SquirrelAwareExecutableNames = v ?? new string[0])
                .SetDescription("Name of one or more SquirrelAware executables.")
                .SetArgumentHelpName("NAME");

            AddOption<FileInfo>("--appIcon", (v) => AppIcon = v)
                .SetDescription("Path to .ico for 'Apps and Features' list.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly()
                .RequiresExtension(".ico");

            if (SquirrelRuntimeInfo.IsWindows) {

                AddOption<string>("--msi", (v) => BuildMsi = v)
                    .SetDescription("Compile a .msi machine-wide deployment tool with the specified bitness.")
                    .SetArgumentHelpName("BITNESS");
            }
        }

        protected static void MustBeValidFrameworkString(OptionResult result)
        {
            for (var i = 0; i < result.Tokens.Count; i++) {
                var framework = result.Tokens[i].Value;
                try {
                    Squirrel.Runtimes.ParseDependencyString(framework);
                } catch (Exception e) {
                    result.ErrorMessage = e.Message;
                }
            }
        }
    }

    public class PackWindowsCommand : ReleasifyWindowsCommand, INugetPackCommand
    {
        public string PackId { get; private set; }

        public string PackVersion { get; private set; }

        public DirectoryInfo PackDirectory { get; private set; }

        public string PackAuthors { get; private set; }

        public string PackTitle { get; private set; }

        public bool IncludePdb { get; private set; }

        public FileInfo ReleaseNotes { get; private set; }

        public PackWindowsCommand()
            : base("pack", "Creates a Squirrel release from a folder containing application files.")
        {
            AddOption<string>(new[] { "--packId", "-u" }, (v) => PackId = v)
                .SetDescription("Unique Id for application bundle.")
                .SetArgumentHelpName("ID")
                .SetRequired()
                .RequiresValidNuGetId();

            // TODO add parser straight to SemanticVersion
            AddOption<string>(new[] { "--packVersion", "-v" }, (v) => PackVersion = v)
                .SetDescription("Current version for application bundle.")
                .SetArgumentHelpName("VERSION")
                .SetRequired()
                .RequiresSemverCompliant();

            AddOption<DirectoryInfo>(new[] { "--packDir", "-p" }, (v) => PackDirectory = v)
                .SetDescription("Directory containing application files for release.")
                .SetArgumentHelpName("DIRECTORY")
                .SetRequired()
                .MustNotBeEmpty();

            AddOption<string>("--packAuthors", (v) => PackAuthors = v)
                .SetDescription("Company name or comma-delimited list of authors.")
                .SetArgumentHelpName("AUTHORS");

            AddOption<string>("--packTitle", (v) => PackTitle = v)
                .SetDescription("Display/friendly name for application.")
                .SetArgumentHelpName("NAME");

            AddOption<bool>("--includePdb", (v) => IncludePdb = v)
                .SetDescription("Add *.pdb files to release package");

            AddOption<FileInfo>("--releaseNotes", (v) => ReleaseNotes = v)
                .SetDescription("File with markdown-formatted notes for this version.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();
        }
    }
}