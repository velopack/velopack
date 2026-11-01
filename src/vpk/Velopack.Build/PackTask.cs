using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Build.Framework;

namespace Velopack.Build;

public class PackTask : VpkTask
{
    public string? TargetRuntime { get; set; }

    [Required]
    [NotNull]
    public string? PackVersion { get; set; }

    [Required]
    [NotNull]
    public string? PackId { get; set; }

    [Required]
    [NotNull]
    public string? PackDirectory { get; set; }

    [Required]
    [NotNull]
    public string? ReleaseDir { get; set; }

    public string? Runtimes { get; set; }

    public string? PackAuthors { get; set; }

    public string? PackTitle { get; set; }

    public string? EntryExecutableName { get; set; }

    public string? Icon { get; set; }

    public string? ReleaseNotes { get; set; }

    public string? DeltaMode { get; set; }

    public string? Channel { get; set; }

    public string? Exclude { get; set; }

    public bool NoDefaultExclude { get; set; }

    public bool NoPortable { get; set; }

    public bool NoInst { get; set; }

    public string? InstWelcome { get; set; }

    public string? InstReadme { get; set; }

    public string? InstLicense { get; set; }
    public string? InstLicenseRtf { get; set; }

    public string? InstConclusion { get; set; }

    //public InstallLocation InstLocation { get; set; } = InstallLocation.Either;

    public string? MsiTopBanner { get; set; }
    public string? MsiDialogBackground { get; set; }

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

    public int? SignParallel { get; set; }

    public string? SignTemplate { get; set; }

    public string? Categories { get; set; }

    public string? Shortcuts { get; set; }

    public string? Compression { get; set; }

    public bool BuildMsi { get; set; }

    public string? MsiVersionOverride { get; set; }

    public string? VelopackBaseUrl { get; set; }

    public double Timeout { get; set; } = 30d;

    protected override async Task<bool> PreExecuteAsync(VpkToolRunner toolRunner, CancellationToken cancellationToken)
    {
        if (DeltaMode == nameof(Packaging.Compression.DeltaMode.None)) {
            return true;
        }

        Log.LogMessage(MessageImportance.High, "DeltaMode is enabled, downloading latest release from Velopack Flow...");

        var options = new Dictionary<string, object?> {
            ["PackageId"] = PackId,
            ["ReleaseDir"] = ReleaseDir,
            ["Timeout"] = Timeout,
        };
        AddIfNotEmpty(options, "Channel", Channel);
        AddIfNotEmpty(options, "VelopackBaseUrl", VelopackBaseUrl);

        Log.LogMessage(MessageImportance.High, "Downloading latest release from Velopack Flow using JSON options...");

        var exitCode = await toolRunner.RunVpkJson(
            ["download", "flow"], options, [], cancellationToken).ConfigureAwait(false);

        if (exitCode != 0) {
            Log.LogWarning($"Failed to download latest release from Velopack Flow (exit code {exitCode}). Delta generation may not work correctly.");
        }

        return true;
    }

    protected override string GetSuccesMessage()
        => $"{PackId} ({PackVersion}) created in {ReleaseDir}";

    protected override string CommandName => "pack";

    protected override Dictionary<string, object?> BuildJsonOptions()
    {
        var options = new Dictionary<string, object?>();
        AddIfNotEmpty(options, "PackId", PackId);
        AddIfNotEmpty(options, "PackVersion", PackVersion);
        AddIfNotEmpty(options, "PackDirectory", PackDirectory);
        AddIfNotEmpty(options, "ReleaseDir", ReleaseDir);
        AddIfNotEmpty(options, "PackAuthors", PackAuthors);
        AddIfNotEmpty(options, "PackTitle", PackTitle);
        AddIfNotEmpty(options, "EntryExecutableName", EntryExecutableName);
        AddIfNotEmpty(options, "Icon", Icon);
        AddIfNotEmpty(options, "ReleaseNotes", ReleaseNotes);
        AddIfNotEmpty(options, "DeltaMode", DeltaMode);
        AddIfNotEmpty(options, "Channel", Channel);
        AddIfNotEmpty(options, "Exclude", Exclude);
        AddIfNotEmpty(options, "TargetRuntime", TargetRuntime);

        AddIfTrue(options, "NoDefaultExclude", NoDefaultExclude);
        AddIfTrue(options, "NoPortable", NoPortable);
        AddIfTrue(options, "NoInst", NoInst);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            AddIfNotEmpty(options, "Runtimes", Runtimes);
            AddIfNotEmpty(options, "SplashImage", SplashImage);
            AddIfNotEmpty(options, "SplashProgressColor", SplashProgressColor);
            AddIfNotEmpty(options, "SignParameters", SignParameters);
            AddIfNotEmpty(options, "SignExclude", SignExclude);
            AddIfNotEmpty(options, "SignTemplate", SignTemplate);
            AddIfNotEmpty(options, "AzureTrustedSignFile", AzureTrustedSignFile);
            AddIfNotEmpty(options, "Shortcuts", Shortcuts);
            AddIfNotEmpty(options, "InstWelcome", InstWelcome);
            AddIfNotEmpty(options, "InstReadme", InstReadme);
            AddIfNotEmpty(options, "InstLicense", InstLicense);
            AddIfNotEmpty(options, "InstConclusion", InstConclusion);
            AddIfNotEmpty(options, "MsiTopBanner", MsiTopBanner);
            AddIfNotEmpty(options, "MsiDialogBackground", MsiDialogBackground);
            AddIfNotEmpty(options, "MsiVersionOverride", MsiVersionOverride);
            AddIfTrue(options, "SkipVelopackAppCheck", SkipVelopackAppCheck);
            AddIfTrue(options, "BuildMsi", BuildMsi);
            if (SignParallel is { } signParallel) {
                options["SignParallel"] = signParallel;
            }
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            AddIfNotEmpty(options, "BundleId", BundleId);
            AddIfNotEmpty(options, "InfoPlistPath", InfoPlistPath);
            AddIfNotEmpty(options, "InstWelcome", InstWelcome);
            AddIfNotEmpty(options, "InstReadme", InstReadme);
            AddIfNotEmpty(options, "InstLicense", InstLicense);
            AddIfNotEmpty(options, "InstConclusion", InstConclusion);
            AddIfNotEmpty(options, "SignAppIdentity", SignAppIdentity);
            AddIfNotEmpty(options, "SignInstallIdentity", SignInstallIdentity);
            AddIfNotEmpty(options, "SignEntitlements", SignEntitlements);
            AddIfNotEmpty(options, "NotaryProfile", NotaryProfile);
            AddIfNotEmpty(options, "Keychain", Keychain);
            AddIfTrue(options, "SignDisableDeep", SignDisableDeep);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            AddIfNotEmpty(options, "Categories", Categories);
            AddIfNotEmpty(options, "Compression", Compression);
        }

        return options;
    }

    private static void AddIfNotEmpty(Dictionary<string, object?> options, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) {
            options[name] = value;
        }
    }

    private static void AddIfTrue(Dictionary<string, object?> options, string name, bool value)
    {
        if (value) {
            options[name] = true;
        }
    }
}
