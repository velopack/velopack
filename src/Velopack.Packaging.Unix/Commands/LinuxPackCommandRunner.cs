using System.Runtime.Versioning;
using ELFSharp.ELF;
using Microsoft.Extensions.Logging;
using Velopack.Packaging.Abstractions;

namespace Velopack.Packaging.Unix.Commands;

[SupportedOSPlatform("linux")]
public class LinuxPackCommandRunner : PackageBuilder<LinuxPackOptions>
{
    protected string PortablePackagePath { get; set; }

    public LinuxPackCommandRunner(ILogger logger, IFancyConsole console)
        : base(RuntimeOs.Linux, logger, console)
    {
    }

    protected override Task<string> PreprocessPackDir(Action<int> progress, string packDir)
    {
        var dir = TempDir.CreateSubdirectory("PreprocessPackDir.AppDir");
        var bin = dir.CreateSubdirectory("usr").CreateSubdirectory("bin");

        if (Options.PackIsAppDir) {
            Log.Info("Using provided .AppDir, will skip building new one.");
            CopyFiles(new DirectoryInfo(Options.PackDirectory), dir, progress, true);
        } else {
            Log.Info("Building new .AppDir");
            var appRunPath = Path.Combine(dir.FullName, "AppRun");

            // app icon
            var icon = Options.Icon ?? HelperFile.GetDefaultAppIcon();
            var iconFilename = Options.PackId + Path.GetExtension(icon);
            File.Copy(icon, Path.Combine(dir.FullName, iconFilename), true);

            File.WriteAllText(appRunPath, $$"""
#!/bin/sh

if [ ! -z "$APPIMAGE" ] && [ ! -z "$APPDIR" ]; then
    MD5=$(echo -n "file://$APPIMAGE" | md5sum | cut -d' ' -f1)
    cp "$APPDIR/{{iconFilename}}" "$HOME/.cache/thumbnails/normal/$MD5.png"
    cp "$APPDIR/{{iconFilename}}" "$HOME/.cache/thumbnails/large/$MD5.png"
    xdg-icon-resource forceupdate
fi

HERE="$(dirname "$(readlink -f "${0}")")"
export PATH="${HERE}"/usr/bin/:"${PATH}"
EXEC=$(grep -e '^Exec=.*' "${HERE}"/*.desktop | head -n 1 | cut -d "=" -f 2 | cut -d " " -f 1 | sed 's/\\s/ /g')
exec "${EXEC}" "$@"
""");
            Chmod.ChmodFileAsExecutable(appRunPath);

            var mainExeName = Options.EntryExecutableName ?? Options.PackId;
            var mainExePath = Path.Combine(packDir, mainExeName);
            if (!File.Exists(mainExePath))
                throw new Exception($"Could not find main executable at '{mainExePath}'. Please specify with --exeName.");

            // spaces should be represented by \s
            // https://specifications.freedesktop.org/desktop-entry-spec/desktop-entry-spec-1.0.html
            mainExeName = mainExeName.Replace(" ", "\\s");

            File.WriteAllText(Path.Combine(dir.FullName, Options.PackId + ".desktop"), $"""
[Desktop Entry]
Type=Application
Name={Options.PackTitle ?? Options.PackId}
Comment={Options.PackTitle ?? Options.PackId} {Options.PackVersion}
Icon={Options.PackId}
Exec={mainExeName}
StartupWMClass={Options.PackId}
Categories=Development;
""");

            // copy existing app files 
            CopyFiles(new DirectoryInfo(packDir), bin, progress, true);
        }

        // velopack required files
        File.WriteAllText(Path.Combine(bin.FullName, "sq.version"), GenerateNuspecContent());
        File.Copy(HelperFile.GetUpdatePath(), Path.Combine(bin.FullName, "UpdateNix"), true);
        progress(100);
        return Task.FromResult(dir.FullName);
    }

    protected override string[] GetMainExeSearchPaths(string packDirectory, string mainExeName)
    {
        return new[] {
            Path.Combine(packDirectory, mainExeName),
            Path.Combine(packDirectory, "usr", "bin", mainExeName),
        };
    }

    protected override Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
    {
        progress(-1);
        var machine = Options.TargetRuntime.HasArchitecture
            ? Options.TargetRuntime.Architecture
            : GetMachineForBinary(MainExePath);
        AppImageTool.CreateLinuxAppImage(packDir, outputPath, machine, Log);
        PortablePackagePath = outputPath;
        progress(100);
        return Task.CompletedTask;
    }

    protected virtual RuntimeCpu GetMachineForBinary(string path)
    {
        var elf = ELFReader.Load(path);

        var machine = elf.Machine switch {
            Machine.AArch64 => RuntimeCpu.arm64,
            Machine.AMD64 => RuntimeCpu.x64,
            Machine.Intel386 => RuntimeCpu.x86,
            _ => throw new Exception($"Unsupported ELF machine type '{elf.Machine}'.")
        };

        return machine;
    }

    protected override Task CreateReleasePackage(Action<int> progress, string packDir, string outputPath)
    {
        var dir = TempDir.CreateSubdirectory("CreateReleasePackage.Linux");
        File.Copy(PortablePackagePath, Path.Combine(dir.FullName, Options.PackId + ".AppImage"), true);
        return base.CreateReleasePackage(progress, dir.FullName, outputPath);
    }

    protected override Task<string> CreateDeltaPackage(Action<int> progress, string releasePkg, string prevReleasePkg, string outputPkg, DeltaMode mode)
    {
        progress(-1); // there is only one "file", so progress will not work
        return base.CreateDeltaPackage(progress, releasePkg, prevReleasePkg, outputPkg, mode);
    }
}
