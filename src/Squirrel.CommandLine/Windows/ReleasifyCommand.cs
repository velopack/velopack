using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;

namespace Squirrel.CommandLine.Windows
{
    public class ReleasifyCommand : ReleaseCommand
    {
        private Option<FileInfo> Package { get; }

        public ReleasifyCommand()
            : base("releasify", "Take an existing nuget package and convert it into a Squirrel release")
        {
            Package = new Option<FileInfo>(new[] { "-p", "--package" }, "{PATH} to a '.nupkg' package to releasify") {
                ArgumentHelpName = "PATH",
                IsRequired = true,
            };
            Package.ExistingOnly().RequiresExtension(".nupkg");
            Add(Package);

            this.SetHandler(Execute);
        }

        //NB: Intentionally hiding base member here.
        private protected new void SetOptionsValues(InvocationContext context, ReleasifyOptions options)
        {
            base.SetOptionsValues(context, options);
            options.package = context.ParseResult.GetValueForOption(Package)?.FullName;
        }

        private void Execute(InvocationContext context)
        {
            var releasifyOptions = new ReleasifyOptions();
            SetOptionsValues(context, releasifyOptions);

            Commands.Releasify(releasifyOptions);
        }
    }
}