using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Unix.Commands
{
    [SupportedOSPlatform("linux")]
    public class LinuxPackCommandRunner : PackageBuilder<LinuxPackOptions>
    {
        protected string PortablePackagePath { get; set; }

        public LinuxPackCommandRunner(ILogger logger)
            : base(RuntimeOs.Linux, logger)
        {
        }

        protected override Task<string> PreprocessPackDir(Action<int> progress, string packDir, string nuspecText)
        {
            var dir = TempDir.CreateSubdirectory("PreprocessPackDir.AppDir");
            var bin = dir.CreateSubdirectory("usr").CreateSubdirectory("bin");

            if (Options.AppDir != null) {
                Log.Info("Using provided .AppDir, will skip building new one.");
                CopyFiles(new DirectoryInfo(Options.AppDir), dir, progress, true);
            } else {
                Log.Info("Building new .AppDir");
                var appRunPath = Path.Combine(dir.FullName, "AppRun");
                File.WriteAllText(appRunPath, """
#!/bin/sh
HERE="$(dirname "$(readlink -f "${0}")")"
export PATH="${HERE}"/usr/bin/:"${PATH}"
EXEC=$(grep -e '^Exec=.*' "${HERE}"/*.desktop | head -n 1 | cut -d "=" -f 2 | cut -d " " -f 1)
exec "${EXEC}" $@
""");
                Chmod.ChmodFileAsExecutable(appRunPath);

                var mainExeName = Options.EntryExecutableName ?? Options.PackId;
                var mainExePath = Path.Combine(packDir, mainExeName);
                if (!File.Exists(mainExePath))
                    throw new Exception($"Could not find main executable at '{mainExePath}'. Please specify with --exeName.");

                File.WriteAllText(Path.Combine(dir.FullName, Options.PackId + ".desktop"), $"""
[Desktop Entry]
Type=Application
Name={Options.PackTitle ?? Options.PackId}
Comment={Options.PackTitle ?? Options.PackId} {Options.PackVersion}
Icon={Options.PackId}
Exec={mainExeName}
Path=~
Categories=Development;
""");

                // copy existing app files 
                CopyFiles(new DirectoryInfo(packDir), bin, progress, true);
            }

            // app icon
            File.Copy(Options.Icon, Path.Combine(dir.FullName, Options.PackId + Path.GetExtension(Options.Icon)), true);
            var helper = new HelperExe(Log);

            // velopack required files
            File.WriteAllText(Path.Combine(bin.FullName, "sq.version"), nuspecText);
            File.Copy(helper.UpdateNixPath, Path.Combine(bin.FullName, "UpdateNix"), true);
            progress(100);
            return Task.FromResult(dir.FullName);
        }

        protected override Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
        {
            progress(-1);
            var helper = new HelperExe(Log);
            helper.CreateLinuxAppImage(packDir, outputPath);
            PortablePackagePath = outputPath;
            progress(100);
            return Task.CompletedTask;
        }

        protected override Task CreateReleasePackage(Action<int> progress, string packDir, string nuspecText, string outputPath)
        {
            var dir = TempDir.CreateSubdirectory("CreateReleasePackage.Linux");
            File.Copy(PortablePackagePath, Path.Combine(dir.FullName, Options.PackId + ".AppImage"), true);
            return base.CreateReleasePackage(progress, dir.FullName, nuspecText, outputPath);
        }

        protected override Task<string> CreateDeltaPackage(Action<int> progress, string releasePkg, string prevReleasePkg, DeltaMode mode)
        {
            progress(-1); // there is only one "file", so progress will not work
            return base.CreateDeltaPackage(progress, releasePkg, prevReleasePkg, mode);
        }
    }
}
