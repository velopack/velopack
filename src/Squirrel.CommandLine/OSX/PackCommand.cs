using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;

namespace Squirrel.CommandLine.OSX
{
    public class PackCommand : BaseCommand
    {
        public Option<string> PackId { get; }
        public Option<string> PackVersion { get; }
        public Option<DirectoryInfo> PackDirectory { get; }
        public Option<string> PackAuthors { get; }
        public Option<string> PackTitle { get; }
        public Option<bool> IncludePdb { get; }
        public Option<FileInfo> ReleaseNotes { get; }
        public Option<string> SquirrelAwareExecutable { get; }
        public Option<FileInfo> Icon { get; }
        public Option<string> BundleId { get; }
        public Option<bool> NoDelta { get; }
        public Option<bool> NoPackage { get; }
        public Option<KeyValuePair<string, FileInfo>[]> PackageContent { get; }
        public Option<string> SigningAppIdentity { get; }
        public Option<string> SigningInstallIdentity { get; }
        public Option<FileInfo> SigningEntitlements { get; }
        public Option<string> NotaryProfile { get; }

        public PackCommand()
            : base("pack", "Creates a Squirrel release from a folder containing application files")
        {
            PackId = new Option<string>(new[] { "--packId", "-u" }, "Unique {ID} for bundle") {
                ArgumentHelpName = "ID",
                IsRequired = true
            };
            PackId.RequiresValidNuGetId();
            Add(PackId);

            PackVersion = new Option<string>(new[] { "--packVersion", "-v" }, "Current {VERSION} for bundle") {
                ArgumentHelpName = "VERSION",
                IsRequired = true
            };
            PackVersion.RequiresSemverCompliant();
            Add(PackVersion);

            PackDirectory = new Option<DirectoryInfo>(new[] { "--packDir", "-p" }, "{DIRECTORY} containing application files for release") {
                ArgumentHelpName = "DIRECTORY",
                IsRequired = true
            };
            PackDirectory.MustNotBeEmpty();
            Add(PackDirectory);

            PackAuthors = new Option<string>("--packAuthors", "Optional company or list of release {AUTHORS}") {
                ArgumentHelpName = "AUTHORS"
            };
            Add(PackAuthors);

            PackTitle = new Option<string>("--packTitle", "Optional display/friendly {NAME} for release") {
                ArgumentHelpName = "NAME"
            };
            Add(PackTitle);

            IncludePdb = new Option<bool>("--includePdb", "Add *.pdb files to release package");
            Add(IncludePdb);

            ReleaseNotes = new Option<FileInfo>("--releaseNotes", "{PATH} to file with markdown notes for version") {
                ArgumentHelpName = "PATH"
            };
            ReleaseNotes.ExistingOnly();
            Add(ReleaseNotes);

            SquirrelAwareExecutable = new Option<string>(new[] { "-e", "--mainExe" }, "The file {NAME} of the main executable") {
                ArgumentHelpName = "NAME"
            };
            Add(SquirrelAwareExecutable);

            Icon = new Option<FileInfo>(new[] { "-i", "--icon" }, "{PATH} to .ico for Setup.exe and Update.exe") {
                ArgumentHelpName = "PATH"
            };
            Icon.ExistingOnly().RequiresExtension(".ico");
            Add(Icon);

            BundleId = new Option<string>("--bundleId", "Override the apple unique {ID} when generating bundles") {
                ArgumentHelpName = "ID"
            };
            Add(BundleId);

            NoDelta = new Option<bool>("--noDelta", "Skip the generation of delta packages");
            Add(NoDelta);

            NoPackage = new Option<bool>("--noPkg", "Skip generating a .pkg installer");
            Add(NoPackage);

            //TODO: Would be nice to setup completions at least for the keys of this option
            PackageContent = new Option<KeyValuePair<string, FileInfo>[]>("--pkgContent", (ArgumentResult value) => {
                var splitCharacters = new[] { '=', ':' };
                var results = new List<KeyValuePair<string, FileInfo>>();
                for (int i = 0; i < value.Tokens.Count; i++) {
                    string token = value.Tokens[i].Value;
                    string[] parts = token.Split(splitCharacters, 2);
                    switch(parts.Length) {
                        case 1:
                            results.Add(new KeyValuePair<string, FileInfo>(parts[0], new FileInfo("")));
                            break;
                        case 2:
                            results.Add(new KeyValuePair<string, FileInfo>(parts[0], new FileInfo(parts[1])));
                            break;
                    }
                }
                return results.ToArray();
            }, false, "Add content files (eg. readme, license) to pkg installer.");
            PackageContent.AddValidator((OptionResult result) => {
                var validContentKeys = new HashSet<string> {
                    "welcome",
                    "readme",
                    "license",
                    "conclusion",
                };
                foreach (var kvp in result.GetValueForOption(PackageContent)) {
                    if (!validContentKeys.Contains(kvp.Key)) {
                        result.ErrorMessage = $"Invalid {PackageContent.Name} key: {kvp.Key}. Must be one of: " + string.Join(", ", validContentKeys);
                    }

                    if (!kvp.Value.Exists) {
                        result.ErrorMessage = $"{PackageContent.Name} file not found: {kvp.Value}";
                    }
                }
            });
            PackageContent.ArgumentHelpName = "key=<FILE>";
            Add(PackageContent);

            SigningAppIdentity = new Option<string>("--signAppIdentity", "The {SUBJECT} name of the cert to use for app code signing") {
                ArgumentHelpName = "SUBJECT"
            };
            Add(SigningAppIdentity);

            SigningInstallIdentity = new Option<string>("--signInstallIdentity", "The {SUBJECT} name of the cert to use for installation packages") {
                ArgumentHelpName = "SUBJECT"
            };
            Add(SigningInstallIdentity);

            SigningEntitlements = new Option<FileInfo>("--signEntitlements", "{PATH} to entitlements file for hardened runtime") {
                ArgumentHelpName = "PATH"
            };
            SigningEntitlements.ExistingOnly().RequiresExtension(".entitlements");
            Add(SigningEntitlements);

            NotaryProfile = new Option<string>("--notaryProfile", "{NAME} of profile containing Apple credentials stored with notarytool") {
                ArgumentHelpName = "NAME"
            };
            Add(NotaryProfile);

            this.SetHandler(Execute);
        }

        private protected void SetOptionsValues(InvocationContext context, PackOptions options)
        {
            base.SetOptionsValues(context, options);
            options.packId = context.ParseResult.GetValueForOption(PackId);
            options.packVersion = context.ParseResult.GetValueForOption(PackVersion);
            options.packDirectory = context.ParseResult.GetValueForOption(PackDirectory)?.FullName;
            options.packAuthors = context.ParseResult.GetValueForOption(PackAuthors);
            options.packTitle = context.ParseResult.GetValueForOption(PackTitle);
            options.includePdb = context.ParseResult.GetValueForOption(IncludePdb);
            options.releaseNotes = context.ParseResult.GetValueForOption(ReleaseNotes)?.FullName;
            options.mainExe = context.ParseResult.GetValueForOption(SquirrelAwareExecutable);
            options.icon = context.ParseResult.GetValueForOption(Icon)?.FullName;
            options.bundleId = context.ParseResult.GetValueForOption(BundleId);
            options.noDelta = context.ParseResult.GetValueForOption(NoDelta);
            options.noPkg = context.ParseResult.GetValueForOption(NoPackage);
            if(context.ParseResult.GetValueForOption(PackageContent) is { } pkgContent) {
                foreach(var kvp in pkgContent) {
                    //NB: If the same key is specified multiple times; last in wins
                    options.pkgContent[kvp.Key] = kvp.Value.FullName;
                }
            }
        }

        private void Execute(InvocationContext context)
        {
            var packOptions = new PackOptions();
            SetOptionsValues(context, packOptions);

            Commands.Pack(packOptions);
        }
    }
}