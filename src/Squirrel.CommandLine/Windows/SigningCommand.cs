using System.CommandLine;
using System.CommandLine.Invocation;

namespace Squirrel.CommandLine.Windows
{
    public class SigningCommand : BaseCommand
    {
        public Option<string> SignParameters { get; }
        public Option<bool> SignSkipDll { get; }
        public Option<int> SignParallel { get; }
        public Option<string> SignTemplate { get; }

        protected SigningCommand(string name, string description)
            : base(name, description)
        {
            SignTemplate = new Option<string>("--signTemplate", "Use a custom signing {COMMAND}. '{{file}}' will be replaced by the path of the file to sign.") {
                ArgumentHelpName = "COMMAND"
            };
            SignTemplate.MustContain("{{file}}");

            if (SquirrelRuntimeInfo.IsWindows) {
                //TODO: Cannot be used with sign template
                SignParameters = new Option<string>(new[] { "-n", "--signParams" }, "Sign files via signtool.exe using these {PARAMETERS}") {
                    ArgumentHelpName = "PARAMETERS"
                };
                this.AreMutuallyExclusive(SignParameters, SignTemplate);
                Add(SignParameters);

                SignSkipDll = new Option<bool>("--signSkipDll", "Only signs EXE files, and skips signing DLL files.");
                Add(SignSkipDll);

                SignParallel = new Option<int>("--signParallel", () => SigningOptions.SignParallelDefault, "The number of files to sign in each call to signtool.exe");
                SignParallel.MustBeBetween(1, 1000);
                Add(SignParallel);
            }
            
            Add(SignTemplate);
        }

        private protected void SetOptionsValues(InvocationContext context, SigningOptions options)
        {
            base.SetOptionsValues(context, options);
            if (SquirrelRuntimeInfo.IsWindows) {
                options.signParams = context.ParseResult.GetValueForOption(SignParameters);
                options.signSkipDll = context.ParseResult.GetValueForOption(SignSkipDll);
                options.signParallel = context.ParseResult.GetValueForOption(SignParallel);
            }
            options.signTemplate = context.ParseResult.GetValueForOption(SignTemplate);
        }
    }
}