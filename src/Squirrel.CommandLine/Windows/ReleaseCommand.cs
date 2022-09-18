using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;

namespace Squirrel.CommandLine.Windows
{
    public class ReleaseCommand : SigningCommand
    {
        protected Option<Uri> BaseUrl { get; }
        //Question this appears to only have an arity of 1, should you be able to specify it multiple times?
        protected Option<string> AddSearchPath { get; }
        protected Option<FileInfo> DebugSetupExe { get; }

        protected Option<bool> NoDelta { get; }
        protected Option<string> Runtimes { get; }
        protected Option<FileInfo> SplashImage { get; }
        protected Option<FileInfo> Icon { get; }
        protected Option<string> SquirrelAwareExecutable { get; }
        protected Option<FileInfo> AppIcon { get; }
        protected Option<Bitness> BuildMsi { get; }

        protected ReleaseCommand(string name, string description)
            : base(name, description)
        {
            BaseUrl = new Option<Uri>(new[] { "-b", "--baseUrl" }, "Provides a base URL to prefix the RELEASES file packages with") {
                IsHidden = true
            };
            BaseUrl.RequiresAbsolute().RequiresScheme(Uri.UriSchemeHttp, Uri.UriSchemeHttps);
            Add(BaseUrl);

            AddSearchPath = new Option<string>("--addSearchPath", "Add additional search directories when looking for helper exe's such as Setup.exe, Update.exe, etc") {
                IsHidden = true
            };
            Add(AddSearchPath);

            DebugSetupExe = new Option<FileInfo>("--debugSetupExe", "Uses the Setup.exe at this {PATH} to create the bundle, and then replaces it with the bundle. " +
                                  "Used for locally debugging Setup.exe with a real bundle attached.") {
                ArgumentHelpName = "PATH",
                IsHidden = true
            };
            Add(DebugSetupExe);

            NoDelta = new Option<bool>("--noDelta", "Skip the generation of delta packages");
            Add(NoDelta);

            Runtimes = new Option<string>(new[] { "-f", "--framework" }, "List of required {RUNTIMES} to install during setup. example: 'net6,vcredist143'") {
                ArgumentHelpName = "RUNTIMES"
            };
            Add(Runtimes);

            SplashImage = new Option<FileInfo>(new[] { "-s", "--splashImage" }, "{PATH} to image/gif displayed during installation") {
                ArgumentHelpName = "PATH"
            };
            SplashImage.ExistingOnly();
            Add(SplashImage);

            Icon = new Option<FileInfo>(new[] { "-i", "--icon" }, "{PATH} to .ico for Setup.exe and Update.exe") {
                ArgumentHelpName = "PATH"
            };
            AppIcon.ExistingOnly().RequiresExtension("ico");
            Add(Icon);

            SquirrelAwareExecutable = new Option<string>(new[] { "-e", "--mainExe" }, "{NAME} of one or more SquirrelAware executables") {
                ArgumentHelpName = "NAME"
            };
            Add(SquirrelAwareExecutable);

            AppIcon = new Option<FileInfo>("--appIcon", "{PATH} to .ico for 'Apps and Features' list") {
                ArgumentHelpName = "PATH"
            };
            AppIcon.ExistingOnly().RequiresExtension("ico");
            Add(AppIcon);

            if (SquirrelRuntimeInfo.IsWindows) {
                BuildMsi = new Option<Bitness>("--msi", "Compile a .msi machine-wide deployment tool with the specified {BITNESS}.") {
                    ArgumentHelpName = "BITNESS"
                };
                Add(BuildMsi);
            }
        }

        private protected void SetOptionsValues(InvocationContext context, ReleasifyOptions options)
        {
            base.SetOptionsValues(context, options);

            options.baseUrl = context.ParseResult.GetValueForOption(BaseUrl)?.AbsoluteUri;
            //TODO: This is a little awkward to set a value as part of parsing
            if (context.ParseResult.GetValueForOption(AddSearchPath) is { } searchPath) {
                HelperFile.AddSearchPath(searchPath);
            }
            options.debugSetupExe = context.ParseResult.GetValueForOption(DebugSetupExe)?.FullName;
            options.noDelta = context.ParseResult.GetValueForOption(NoDelta);
            options.framework = context.ParseResult.GetValueForOption(Runtimes);
            options.splashImage = context.ParseResult.GetValueForOption(SplashImage)?.FullName;
            options.icon = context.ParseResult.GetValueForOption(Icon)?.FullName;
            //TODO: This is a little awkward to set a value as part of parsing
            if (context.ParseResult.GetValueForOption(SquirrelAwareExecutable) is { } mainExe) {
                options.mainExes.Add(mainExe);
            }
            options.appIcon = context.ParseResult.GetValueForOption(AppIcon)?.FullName;

            if (SquirrelRuntimeInfo.IsWindows) {
                switch (context.ParseResult.GetValueForOption(BuildMsi)) {
                case Bitness.x86:
                    options.msi = "x86";
                    break;
                case Bitness.x64:
                    options.msi = "x64";
                    break;
                }
            }
        }
    }
}