using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Velopack.Packaging;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;

namespace Velopack.Build;

public class PackTask : MSBuildAsyncTask
{
    public bool SelfContained { get; set; }

    public string? TargetRuntime { get; set; }

    [Required]
    public string TargetFramework { get; set; } = null!;

    [Required]
    public string PackVersion { get; set; } = "";

    [Required]
    public string PackId { get; set; } = "";

    [Required]
    public string PackDirectory { get; set; } = null!;

    [Required]
    public string ReleaseDir { get; set; } = null!;

    public string Runtimes { get; set; } = "";

    public string? PackAuthors { get; set; }

    public string? PackTitle { get; set; }

    public string? EntryExecutableName { get; set; }

    public string? Icon { get; set; }

    public string? ReleaseNotes { get; set; }

    public string? DeltaMode { get; set; } = "BestSpeed";

    public string? Channel { get; set; }

    public string? Exclude { get; set; }

    public bool NoPortable { get; private set; }

    public bool NoInst { get; private set; }

    public string? InstWelcome { get; set; }

    public string? InstReadme { get; set; }

    public string? InstLicense { get; set; }

    public string? InstConclusion { get; set; }

    public string? SignAppIdentity { get; set; }

    public string? SignInstallIdentity { get; set; }

    public string? SignEntitlements { get; set; }

    public string? NotaryProfile { get; set; }

    public string? Keychain { get; set; }

    public string? BundleId { get; set; }

    public string? InfoPlistPath { get; set; }

    public string? SplashImage { get; set; }

    public bool SkipVelopackAppCheck { get; set; }

    public string? SignParameters { get; set; }
    public string? AzureTrustedSignFile { get; set; }

    public string? SignExclude { get; set; }

    public int SignParallel { get; set; } = 10;

    public string? SignTemplate { get; set; }

    public string? Categories { get; set; }

    public string? Shortcuts { get; set; }
    
    public string? Compression { get; set; }

    public bool BuildMsi { get; set; }

    public string? MsiVersionOverride { get; set; }

    protected override async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        //System.Diagnostics.Debugger.Launch();
        try {
            HelperFile.ClearSearchPaths();
            var searchPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "..", "..", "vendor"));
            HelperFile.AddSearchPath(searchPath);

            if (VelopackRuntimeInfo.IsWindows) {
                var options = this.ToWinPackOptions();

                // we can auto-compute the requires runtimes on Windows from the TFM/RID
                if (!SelfContained && String.IsNullOrEmpty(options.Runtimes)) {
#pragma warning disable CS0618 // Type or member is obsolete
                    if (TargetFramework.Contains("-")) {
                        TargetFramework = TargetFramework.Substring(0, TargetFramework.IndexOf('-'));
                    }
                    var runtime = Windows.Runtimes.GetRuntimeByName(TargetFramework);
                    if (runtime is Windows.Runtimes.FrameworkInfo) {
                        options.Runtimes = runtime.Id;
                        Log.LogMessage(MessageImportance.High, $"Setup.exe will automatically bootstrap {runtime.DisplayName}.");
                    } else if (!SelfContained && runtime is Windows.Runtimes.DotnetInfo dni && options.TargetRuntime?.HasArchitecture == true) {
                        var rt = new Windows.Runtimes.DotnetInfo(dni.MinVersion.Version, options.TargetRuntime.Architecture);
                        options.Runtimes = rt.Id;
                        Log.LogMessage(MessageImportance.High, $"Setup.exe will automatically bootstrap {rt.DisplayName}.");
                    } else {
                        Log.LogWarning($"No runtime specified and no default runtime found for TFM '{TargetFramework}', please specify runtimes via VelopackRuntimes property in your csproj.");
                    }
#pragma warning restore CS0618 // Type or member is obsolete
                }

                var runner = new WindowsPackCommandRunner(Logger, Logger);
                await runner.Run(options).ConfigureAwait(false);
            } else if (VelopackRuntimeInfo.IsOSX) {
                var options = this.ToOsxPackOptions();
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
        } catch (Exception ex) {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }
}
