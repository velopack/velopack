using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Velopack.Packaging;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;

namespace Velopack.Build;

public class PackTask : MSBuildAsyncTask
{
    public string? TargetRuntime { get; set; }

    [Required]
    public string PackVersion { get; set; } = "";

    [Required]
    public string Runtimes { get; set; } = "";

    [Required]
    public string PackId { get; set; } = "";

    [Required]
    public string PackDirectory { get; set; } = null!;

    [Required]
    public string ReleaseDir { get; set; } = null!;

    public string? PackAuthors { get; set; }

    public string? PackTitle { get; set; }

    public string? EntryExecutableName { get; set; }

    public string? Icon { get; set; }

    public string? ReleaseNotes { get; set; }

    public DeltaMode DeltaMode { get; set; } = DeltaMode.BestSpeed;

    public string? Channel { get; set; }

    public bool PackIsAppDir { get; set; }

    public bool IncludePdb { get; set; }

    public bool NoPackage { get; set; }

    public string? PackageWelcome { get; set; }

    public string? PackageReadme { get; set; }

    public string? PackageLicense { get; set; }

    public string? PackageConclusion { get; set; }

    public string? SigningAppIdentity { get; set; }

    public string? SigningInstallIdentity { get; set; }

    public string? SigningEntitlements { get; set; }

    public string? NotaryProfile { get; set; }

    public string? BundleId { get; set; }

    public string? InfoPlistPath { get; set; }

    public string? SplashImage { get; set; }

    public bool SkipVelopackAppCheck { get; set; }

    public string? SignParameters { get; set; }

    public bool SignSkipDll { get; set; }

    public int SignParallel { get; set; } = 10;

    public string? SignTemplate { get; set; }

    protected override async Task<bool> ExecuteAsync()
    {
        //System.Diagnostics.Debugger.Launch();
        HelperFile.ClearSearchPaths();
        HelperFile.AddSearchPath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", "..", "vendor");

        if (VelopackRuntimeInfo.IsWindows) {
            var options = this.ToWinPackOptions();
            var runner = new WindowsPackCommandRunner(Logger, Logger);
            await runner.Run(options).ConfigureAwait(false);
        } else if (VelopackRuntimeInfo.IsOSX) {
            var options = this.ToOSXPackOptions();
            var runner = new OsxPackCommandRunner(Logger, Logger);
            await runner.Run(options).ConfigureAwait(false);

        } else if (VelopackRuntimeInfo.IsLinux) {
            var options = this.ToLinuxPackOptions();
            var runner = new LinuxPackCommandRunner(Logger, Logger);
            await runner.Run(options).ConfigureAwait(false);
        } else {
            throw new NotSupportedException("Unsupported OS platform: " + VelopackRuntimeInfo.SystemOs.GetOsLongName());
        }

        Log.LogMessage(MessageImportance.High, $"{PackId} ({PackVersion}) created in {Path.GetFullPath(ReleaseDir)}");
        return true;
    }
}
