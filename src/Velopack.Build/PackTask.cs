using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Velopack.Packaging;
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
    public string ReleaseDirectory { get; set; } = null!;

    protected override async Task<bool> ExecuteAsync()
    {
        //System.Diagnostics.Debugger.Launch();
        HelperFile.AddSearchPath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

        if (VelopackRuntimeInfo.IsWindows) {

            var targetRuntime = RID.Parse(TargetRuntime ?? VelopackRuntimeInfo.SystemOs.GetOsShortName());
            if (targetRuntime.BaseRID == RuntimeOs.Unknown) {
                //TODO: handle this error case
            }

            DirectoryInfo releaseDir = new(ReleaseDirectory);
            releaseDir.Create();

            var runner = new WindowsPackCommandRunner(Logger, Logger);
            await runner.Run(new WindowsPackOptions() {
                PackId = PackId,
                ReleaseDir = releaseDir,
                PackDirectory = PackDirectory,
                Runtimes = Runtimes,
                TargetRuntime = targetRuntime,
                PackVersion = PackVersion,
            }).ConfigureAwait(false);

            Log.LogMessage(MessageImportance.High, $"{PackId} ({PackVersion}) created in {ReleaseDirectory}");
        } else if (VelopackRuntimeInfo.IsOSX) {
            //TODO: Implement

        } else if (VelopackRuntimeInfo.IsLinux) {
            //TODO: Implement

        } else {
            //TODO: Do we really want to fail to pack (effectively failing the user's publish, or should we just warn?
            throw new NotSupportedException("Unsupported OS platform: " + VelopackRuntimeInfo.SystemOs.GetOsLongName());
        }
        return true;
    }
}
