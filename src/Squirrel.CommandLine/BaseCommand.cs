using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine
{
    public class BaseCommand : Command
    {
        protected static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(BaseOptions));

        public Option<DirectoryInfo> ReleaseDirectory { get; }

        protected BaseCommand(string name, string description)
            : base(name, description)
        {
            ReleaseDirectory = new Option<DirectoryInfo>(new[] { "-r", "--releaseDir" }, "Output directory for Squirrel packages") {
                ArgumentHelpName = "DIRECTORY"
            };
            Add(ReleaseDirectory);
        }

        private protected void SetOptionsValues(InvocationContext context, BaseOptions options)
        {
            options.releaseDir = context.ParseResult.GetValueForOption(ReleaseDirectory)?.FullName;
        }
    }
}