using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.NET.HostModel.Bundle;
using Octokit;
using Velopack.Packaging;

namespace Velopack.Vpk.Commands
{
    public class LinuxPackCommand : PlatformCommand
    {
        public string PackId { get; private set; }

        public string PackVersion { get; private set; }

        public string PackDirectory { get; private set; }

        public string PackAuthors { get; private set; }

        public string PackTitle { get; private set; }

        public string EntryExecutableName { get; private set; }

        public string Icon { get; private set; }

        public string ReleaseNotes { get; set; }

        public bool PackIsAppDir { get; private set; }

        public DeltaMode Delta { get; set; } = DeltaMode.BestSpeed;

        public LinuxPackCommand()
            : this("pack", "Create's a Linux .AppImage bundle from a folder containing application files.")
        { }

        public LinuxPackCommand(string name, string description)
            : base(name, description)
        {
            AddOption<string>((v) => PackId = v, "--packId", "-u")
                .SetDescription("Unique Id for application bundle.")
                .SetArgumentHelpName("ID")
                .SetRequired()
                .RequiresValidNuGetId();

            // TODO add parser straight to SemanticVersion?
            AddOption<string>((v) => PackVersion = v, "--packVersion", "-v")
                .SetDescription("Current version for application bundle.")
                .SetArgumentHelpName("VERSION")
                .SetRequired()
                .RequiresSemverCompliant();

            var packDir = AddOption<DirectoryInfo>((v) => PackDirectory = v.ToFullNameOrNull(), "--packDir", "-p")
                .SetDescription("Directory containing application files from dotnet publish")
                .SetArgumentHelpName("DIR")
                .MustNotBeEmpty();

            AddOption<string>((v) => PackAuthors = v, "--packAuthors")
                .SetDescription("Company name or comma-delimited list of authors.")
                .SetArgumentHelpName("AUTHORS");

            AddOption<string>((v) => PackTitle = v, "--packTitle")
                .SetDescription("Display/friendly name for application.")
                .SetArgumentHelpName("NAME");

            AddOption<string>((v) => EntryExecutableName = v, "-e", "--mainExe")
                .SetDescription("The file name of the main/entry executable.")
                .SetArgumentHelpName("NAME");

            var icon = AddOption<FileInfo>((v) => Icon = v.ToFullNameOrNull(), "-i", "--icon")
                .SetDescription("Path to the icon file for this bundle.")
                .SetArgumentHelpName("PATH")
                .MustExist();

            AddOption<FileInfo>((v) => ReleaseNotes = v.ToFullNameOrNull(), "--releaseNotes")
                .SetDescription("File with markdown-formatted notes for this version.")
                .SetArgumentHelpName("PATH")
                .MustExist();

            AddOption<DeltaMode>((v) => Delta = v, "--delta")
                .SetDefault(DeltaMode.BestSpeed)
                .SetDescription("Set the delta generation mode.");

            var appDir = AddOption<DirectoryInfo>((v) => {
                    PackDirectory = v.ToFullNameOrNull();
                    PackIsAppDir = true;
                }, "--appDir")
                .SetDescription("Directory containing application in .AppDir format")
                .SetArgumentHelpName("DIR")
                .MustNotBeEmpty();

            this.AreMutuallyExclusive(packDir, appDir);
            this.AtLeastOneRequired(packDir, appDir);
            this.AreMutuallyExclusive(icon, appDir);
            this.AtLeastOneRequired(icon, appDir);
        }
    }
}
