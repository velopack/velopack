using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;

namespace Squirrel.CommandLine.Commands
{
    public class BaseCommand : Command
    {
        public string TargetRuntime { get; set; }

        public string ReleaseDirectory { get; private set; }

        protected Option<DirectoryInfo> ReleaseDirectoryOption { get; private set; }

        //protected static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(BaseCommand));
        private Dictionary<Option, Action<ParseResult>> _setters = new();

        protected BaseCommand(string name, string description)
            : base(name, description)
        {
            ReleaseDirectoryOption = AddOption<DirectoryInfo>((v) => ReleaseDirectory = v.ToFullNameOrNull(), "-o", "--outputDir")
                .SetDescription("Output directory for Squirrel packages.")
                .SetArgumentHelpName("DIR")
                .SetDefault(new DirectoryInfo(".\\Releases"));
        }

        public DirectoryInfo GetReleaseDirectory()
        {
            var di = new DirectoryInfo(ReleaseDirectory);
            if (!di.Exists) di.Create();
            return di;
        }

        protected virtual Option<T> AddOption<T>(Action<T> setValue, params string[] aliases)
        {
            return AddOption(setValue, new Option<T>(aliases));
        }

        protected virtual Option<T> AddOption<T>(Action<T> setValue, Option<T> opt)
        {
            _setters[opt] = (ctx) => setValue(ctx.GetValueForOption(opt));
            Add(opt);
            return opt;
        }

        public virtual void SetProperties(ParseResult context)
        {
            foreach (var kvp in _setters) {
                if (context.Errors.Any(e => e.SymbolResult?.Symbol?.Equals(kvp.Key) == true)) {
                    continue; // skip setting values for options with errors
                }
                kvp.Value(context);
            }
        }

        public virtual ParseResult ParseAndApply(string command)
        {
            var x = this.Parse(command);
            SetProperties(x);
            return x;
        }
    }

    public interface INugetPackCommand
    {
        string PackId { get; }
        string PackVersion { get; }
        string PackDirectory { get; }
        string PackAuthors { get; }
        string PackTitle { get; }
        bool IncludePdb { get; }
        string ReleaseNotes { get; }
    }
}