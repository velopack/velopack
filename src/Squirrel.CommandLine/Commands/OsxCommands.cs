using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;

namespace Squirrel.CommandLine.Commands
{
    public class PackOsxCommand : BaseCommand, INugetPackCommand
    {
        public string PackId { get; private set; }

        public string PackVersion { get; private set; }

        public DirectoryInfo PackDirectory { get; private set; }

        public string PackAuthors { get; private set; }

        public string PackTitle { get; private set; }

        public bool IncludePdb { get; private set; }

        public FileInfo ReleaseNotes { get; private set; }

        public string EntryExecutableName { get; private set; }

        public FileInfo Icon { get; private set; }

        public string BundleId { get; private set; }

        public bool NoDelta { get; private set; }

        public bool NoPackage { get; private set; }

        public Dictionary<string, FileInfo> PackageExtraContent { get; private set; }

        public string SigningAppIdentity { get; private set; }

        public string SigningInstallIdentity { get; private set; }

        public FileInfo SigningEntitlements { get; private set; }

        public string NotaryProfile { get; private set; }

        public PackOsxCommand()
            : base("packosx", "Creates a Squirrel release from a folder containing application files.")
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
                .SetDescription("Add *.pdb files to release package.");

            AddOption<FileInfo>("--releaseNotes", (v) => ReleaseNotes = v)
                .SetDescription("File with markdown-formatted notes for this version.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<string>(new[] { "-e", "--mainExe" }, (v) => EntryExecutableName = v)
                .SetDescription("The file name of the main/entry executable.")
                .SetArgumentHelpName("NAME");

            AddOption<FileInfo>(new[] { "-i", "--icon" }, (v) => Icon = v)
                .SetDescription("Path to the .icns file for this bundle.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly()
                .RequiresExtension(".icns");

            AddOption<string>("--bundleId", (v) => BundleId = v)
                .SetDescription("Override the apple unique Id when generating bundles.")
                .SetArgumentHelpName("ID");

            AddOption<bool>("--noDelta", (v) => NoDelta = v)
                .SetDescription("Skip the generation of delta packages.");

            AddOption<bool>("--noPkg", (v) => NoPackage = v)
                .SetDescription("Skip generating a .pkg installer.");

            ParseArgument<Dictionary<string, FileInfo>> parsePkgContent = (ArgumentResult value) => {
                var splitCharacters = new[] { '=', ':' };
                var results = new Dictionary<string, FileInfo>();
                for (int i = 0; i < value.Tokens.Count; i++) {
                    string token = value.Tokens[i].Value;
                    string[] parts = token.Split(splitCharacters, 2);
                    switch (parts.Length) {
                    case 1:
                        results[parts[0]] = new FileInfo("");
                        break;
                    case 2:
                        results[parts[0]] = new FileInfo(parts[1]);
                        break;
                    }
                }
                return results;
            };

            //TODO: Would be nice to setup completions at least for the keys of this option
            var pkgContent = AddOption<Dictionary<string, FileInfo>>("--pkgContent", (v) => PackageExtraContent = v, parsePkgContent)
                .SetDescription("Add content files (eg. readme, license) to pkg installer.")
                .SetArgumentHelpName("key=<FILE>");

            pkgContent.AddValidator((OptionResult result) => {
                var validContentKeys = new HashSet<string> {
                    "welcome",
                    "readme",
                    "license",
                    "conclusion",
                };
                foreach (var kvp in result.GetValueForOption(pkgContent)) {
                    if (!validContentKeys.Contains(kvp.Key)) {
                        result.ErrorMessage = $"Invalid {pkgContent.Name} key: {kvp.Key}. Must be one of: " + string.Join(", ", validContentKeys);
                    }

                    if (!kvp.Value.Exists) {
                        result.ErrorMessage = $"{pkgContent.Name} file not found: {kvp.Value}";
                    }
                }
            });

            AddOption<string>("--signAppIdentity", (v) => SigningAppIdentity = v)
                .SetDescription("The subject name of the cert to use for app code signing.")
                .SetArgumentHelpName("SUBJECT");

            AddOption<string>("--signInstallIdentity", (v) => SigningInstallIdentity = v)
                .SetDescription("The subject name of the cert to use for installation packages.")
                .SetArgumentHelpName("SUBJECT");

            AddOption<FileInfo>("--signEntitlements", (v) => SigningEntitlements = v)
                .SetDescription("Path to entitlements file for hardened runtime signing.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly()
                .RequiresExtension(".entitlements");

            AddOption<string>("--notaryProfile", (v) => NotaryProfile = v)
                .SetDescription("Name of profile containing Apple credentials stored with notarytool.")
                .SetArgumentHelpName("NAME");
        }
    }
}