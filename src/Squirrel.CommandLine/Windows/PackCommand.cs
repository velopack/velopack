using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;

namespace Squirrel.CommandLine.Windows
{
    public class PackCommand : ReleaseCommand
    {
        public Option<string> PackName { get; }
        public Option<DirectoryInfo> PackDirectoryObsolete { get; }
        public Option<DirectoryInfo> PackDirectory { get; }
        public Option<string> PackId { get; }
        public Option<string> PackVersion { get; }
        public Option<string> PackTitle { get; }
        public Option<string> PackAuthors { get; }
        public Option<bool> IncludePdb { get; }
        public Option<FileInfo> ReleaseNotes { get; }

        public PackCommand()
            : base("pack", "Creates a Squirrel release from a folder containing application files")
        {
            PackId = new Option<string>(new[] { "-u", "--packId" }, "Unique {ID} for release") {
                ArgumentHelpName = "ID"
            };
            PackId.RequiresValidNuGetId();
            Add(PackId);

            PackName = new Option<string>("--packName", $"The name of the package to create. This is deprecated, use {PackId.Name} instead.") {
                IsHidden = true,
            };
            Add(PackName);

            this.RequiredAllowObsoleteFallback(PackId, PackName);

            PackDirectoryObsolete = new Option<DirectoryInfo>("--packDirectory", "Obsolete, use --packDir instead");
            PackDirectoryObsolete.MustNotBeEmpty();
            Add(PackDirectoryObsolete);
            
            PackDirectory = new Option<DirectoryInfo>(new[] { "--packDir", "-p" }, "{DIRECTORY} containing application files for release") {
                ArgumentHelpName = "DIRECTORY"
            };
            PackDirectory.MustNotBeEmpty();
            Add(PackDirectory);

            PackVersion = new Option<string>(new[] { "-v", "--packVersion" }, "Current {VERSION} for release") {
                ArgumentHelpName = "VERSION",
                IsRequired = true,
            };
            PackVersion.RequiresSemverCompliant();
            Add(PackVersion);

            PackTitle = new Option<string>("--packTitle", "Optional display/friendly {NAME} for release") {
                ArgumentHelpName = "NAME"
            };
            Add(PackTitle);

            PackAuthors = new Option<string>("--packAuthors", "Optional company or list of release {AUTHORS}") {
                ArgumentHelpName = "AUTHORS"
            };
            Add(PackAuthors);

            IncludePdb = new Option<bool>("--includePdb", "Add *.pdb files to release package");
            Add(IncludePdb);

            ReleaseNotes = new Option<FileInfo>("--releaseNotes", "{PATH} to file with markdown notes for version") {
                ArgumentHelpName = "PATH"
            };
            ReleaseNotes.ExistingOnly();
            Add(ReleaseNotes);


            this.SetHandler(Execute);
        }

        private protected void SetOptionsValues(InvocationContext context, PackOptions options)
        {
            options.packId = context.ParseResult.GetValueForOption(PackId);
            if (string.IsNullOrEmpty(options.packId) && context.ParseResult.GetValueForOption(PackName) is { } packName) {
                options.packId = packName;
                Log.Warn("--packName is deprecated. Use --packId instead.");
            }
            options.packVersion = context.ParseResult.GetValueForOption(PackVersion);
            options.packDirectory = context.ParseResult.GetValueForOption(PackDirectory)?.FullName;
            options.packTitle = context.ParseResult.GetValueForOption(PackTitle);
            options.packAuthors = context.ParseResult.GetValueForOption(PackAuthors);
            options.includePdb = context.ParseResult.GetValueForOption(IncludePdb);
            options.releaseNotes = context.ParseResult.GetValueForOption(ReleaseNotes)?.FullName;
        }

        private void Execute(InvocationContext context)
        {
            var packOptions = new PackOptions();
            SetOptionsValues(context, packOptions);
            //TODO: Would be nice to just be able to make the option required, but the requirement from the
            // previous code allows multiple options to set the packId property.
            if (string.IsNullOrEmpty(packOptions.packId))
                throw new InvalidOperationException($"'{PackId.Name}' is required");

            Commands.Pack(packOptions);
        }
    }
}