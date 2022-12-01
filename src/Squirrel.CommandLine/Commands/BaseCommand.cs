using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine.Commands
{
    public class BaseCommand : Command
    {
        public DirectoryInfo ReleaseDirectory { get; private set; }

        protected Option<DirectoryInfo> ReleaseDirectoryOption { get; private set; }

        protected static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(BaseCommand));
        private Action<InvocationContext> _setters;

        protected BaseCommand(string name, string description, bool releaseDirMustNotBeEmpty = false)
            : base(name, description)
        {
            ReleaseDirectoryOption = AddOption<DirectoryInfo>(new[] { "-r", "--releaseDir" }, (v) => ReleaseDirectory = v)
                .SetDescription("Output directory for Squirrel packages.")
                .SetArgumentHelpName("DIRECTORY");
            ReleaseDirectoryOption.SetDefaultValue(new DirectoryInfo(".\\Releases"));
        }

        public DirectoryInfo GetReleaseDirectory()
        {
            if (ReleaseDirectory == null) ReleaseDirectory = new DirectoryInfo(".\\Releases");
            if (!ReleaseDirectory.Exists) ReleaseDirectory.Create();
            return ReleaseDirectory;
        }

        protected virtual Option<T> AddOption<T>(string alias, Action<T> setValue)
        {
            return AddOption(new[] { alias }, setValue);
        }

        protected virtual Option<T> AddOption<T>(string[] aliases, Action<T> setValue)
        {
            return AddOption(new Option<T>(aliases), setValue);
        }

        protected virtual Option<T> AddOption<T>(string alias, Action<T> setValue, ParseArgument<T> parseArgument)
        {
            return AddOption(new[] { alias }, setValue, parseArgument);
        }

        protected virtual Option<T> AddOption<T>(string[] aliases, Action<T> setValue, ParseArgument<T> parseArgument)
        {
            return AddOption(new Option<T>(aliases, parseArgument), setValue);
        }

        protected virtual Option<T> AddOption<T>(Option<T> opt, Action<T> setValue)
        {
            _setters += (ctx) => setValue(ctx.ParseResult.GetValueForOption(opt));
            Add(opt);
            return opt;
        }

        public virtual void SetProperties(InvocationContext context)
        {
            _setters?.Invoke(context);
        }
    }

    public interface INugetPackCommand
    {
        string PackId { get; }
        string PackVersion { get; }
        DirectoryInfo PackDirectory { get; }
        string PackAuthors { get; }
        string PackTitle { get; }
        bool IncludePdb { get; }
        FileInfo ReleaseNotes { get; }
    }
}