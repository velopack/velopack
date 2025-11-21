using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

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

    public string? Runtimes { get; set; }

    public string? PackAuthors { get; set; }

    public string? PackTitle { get; set; }

    public string? EntryExecutableName { get; set; }

    public string? Icon { get; set; }

    public string? ReleaseNotes { get; set; }

    public string? DeltaMode { get; set; } = "BestSpeed";

    public string? Channel { get; set; }

    public string? Exclude { get; set; }

    public bool NoPortable { get; set; }

    public bool NoInst { get; set; }

    public string? InstWelcome { get; set; }

    public string? InstReadme { get; set; }

    public string? InstLicense { get; set; }
    public string? InstLicenseRtf { get; set; }

    public string? InstConclusion { get; set; }

    public InstallLocation InstLocation { get; set; } = InstallLocation.Either;

    public string? MsiBanner { get; set; }
    public string? MsiLogo { get; set; }

    public string? SignAppIdentity { get; set; }

    public string? SignInstallIdentity { get; set; }

    public string? SignEntitlements { get; set; }
    
    public bool SignDisableDeep { get; set; }

    public string? NotaryProfile { get; set; }

    public string? Keychain { get; set; }

    public string? BundleId { get; set; }

    public string? InfoPlistPath { get; set; }

    public string? SplashImage { get; set; }

    public string? SplashProgressColor { get; set; }

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

    // Tool configuration properties
    public string VelopackToolMode { get; set; } = "Auto";
    public string? VelopackToolVersion { get; set; }
    public bool VelopackToolPrerelease { get; set; }
    public string? VelopackToolSource { get; set; }
    public bool VelopackSkipToolInstall { get; set; }

    protected override async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        try {
            // Resolve VPK tool
            var resolver = new VpkToolResolver(Log);
            var config = new VpkToolConfiguration
            {
                Mode = ParseToolMode(VelopackToolMode),
                Version = VelopackToolVersion,
                AllowPrerelease = VelopackToolPrerelease,
                Source = VelopackToolSource,
                SkipInstall = VelopackSkipToolInstall,
                WorkingDirectory = System.Environment.CurrentDirectory
            };

            var tool = await resolver.ResolveToolAsync(config, cancellationToken);
            var toolRunner = new DotNetToolRunner(Log);

            // Build VPK pack command arguments
            var args = BuildPackArguments();

            Log.LogMessage(MessageImportance.High, $"Executing: dotnet {tool.ExecutionPrefix} {string.Join(" ", args)}");

            // Run VPK tool
            var exitCode = await toolRunner.RunToolAsync("vpk", args, tool.IsLocal, config.WorkingDirectory, null, cancellationToken);

            if (exitCode == 0)
            {
                Log.LogMessage(MessageImportance.High, $"{PackId} ({PackVersion}) created in {ReleaseDir}");
                return true;
            }
            else
            {
                Log.LogError($"VPK tool exited with code {exitCode}");
                return false;
            }
        } catch (Exception ex) {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private string[] BuildPackArguments()
    {
        var builder = new ArgumentBuilder();
        
        // Add pack command
        builder.AddCommand("pack");

        // Required arguments
        builder.AddOption("--packId", PackId);
        builder.AddOption("--packVersion", PackVersion);
        builder.AddOption("--packDir", PackDirectory);
        builder.AddOption("--outputDir", ReleaseDir);

        // Optional arguments
        builder.AddOption("--mainExe", EntryExecutableName);
        builder.AddOption("--packAuthors", PackAuthors);
        builder.AddOption("--packTitle", PackTitle);
        builder.AddOption("--icon", Icon);
        builder.AddOption("--releaseNotes", ReleaseNotes);
        builder.AddOption("--delta", DeltaMode);
        builder.AddOption("--channel", Channel);
        builder.AddOption("--exclude", Exclude);
        builder.AddOption("--framework", Runtimes);
        builder.AddOption("--splashImage", SplashImage);
        builder.AddOption("--signParams", SignParameters);
        builder.AddOption("--signTemplate", SignTemplate);
        builder.AddOption("--signExclude", SignExclude);
        builder.AddOption("--signParallel", SignParallel, defaultValue: 10);
        builder.AddOption("--shortcuts", Shortcuts);
        builder.AddOption("--categories", Categories);
        
        // macOS specific
        builder.AddOption("--signAppIdentity", SignAppIdentity);
        builder.AddOption("--signInstallIdentity", SignInstallIdentity);
        builder.AddOption("--signEntitlements", SignEntitlements);
        builder.AddOption("--notaryProfile", NotaryProfile);
        builder.AddOption("--keychain", Keychain);
        builder.AddOption("--bundleId", BundleId);
        
        // Boolean flags
        builder.AddOption("--skipVeloAppCheck", SkipVelopackAppCheck);
        builder.AddOption("--noPortable", NoPortable);
        builder.AddOption("--noInst", NoInst);
        builder.AddOption("--msiDeploymentTool", BuildMsi);
        builder.AddOption("--signDisableDeep", SignDisableDeep);

        return builder.Build();
    }

    private static VpkToolConfiguration.ToolMode ParseToolMode(string mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "local" => VpkToolConfiguration.ToolMode.Local,
            "global" => VpkToolConfiguration.ToolMode.Global,
            _ => VpkToolConfiguration.ToolMode.Auto
        };
    }
}
