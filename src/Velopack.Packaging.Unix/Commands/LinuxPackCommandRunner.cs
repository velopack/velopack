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
            // app icon
            File.Copy(Options.Icon, Path.Combine(dir.FullName, Options.PackId + Path.GetExtension(Options.Icon)), true);
        }

        // velopack required files
        File.WriteAllText(Path.Combine(bin.FullName, "sq.version"), GenerateNuspecContent());
        File.Copy(HelperFile.GetUpdatePath(), Path.Combine(bin.FullName, "UpdateNix"), true);
        progress(100);
        return Task.FromResult(dir.FullName);
    }

    protected override string[] GetMainExeSearchPaths(string packDirectory, string mainExeName)
    {
        return base.GetMainExeSearchPaths(packDirectory, mainExeName)
            .Concat(new[] { Path.Combine(packDirectory, "usr", "bin", mainExeName) })
            .ToArray();
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
