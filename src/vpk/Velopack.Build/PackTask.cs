using Microsoft.Build.Framework;

namespace Velopack.Build;

public class PackTask : VpkTask
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

    protected override string GetSuccesMessage()
        => $"{PackId} ({PackVersion}) created in {ReleaseDir}";

    protected override string[] BuildArguments()
    {
        IEnumerable<string> GetArguments()
        {
            yield return "pack";
            yield return "--legacyConsole";
            yield return "--yes";

            if (!string.IsNullOrWhiteSpace(PackId))
            {
                yield return "--packId";
                yield return PackId;
            }

            if (!string.IsNullOrWhiteSpace(PackVersion))
            {
                yield return "--packVersion";
                yield return PackVersion;
            }

            if (!string.IsNullOrWhiteSpace(PackDirectory))
            {
                yield return "--packDir";
                yield return PackDirectory;
            }

            if (!string.IsNullOrWhiteSpace(ReleaseDir))
            {
                yield return "--outputDir";
                yield return ReleaseDir;
            }

            if (!string.IsNullOrWhiteSpace(EntryExecutableName))
            {
                yield return "--mainExe";
                yield return EntryExecutableName!;
            }

            if (!string.IsNullOrWhiteSpace(PackAuthors))
            {
                yield return "--packAuthors";
                yield return PackAuthors!;
            }

            if (!string.IsNullOrWhiteSpace(PackTitle))
            {
                yield return "--packTitle";
                yield return PackTitle!;
            }

            if (!string.IsNullOrWhiteSpace(Icon))
            {
                yield return "--icon";
                yield return Icon!;
            }

            if (!string.IsNullOrWhiteSpace(ReleaseNotes))
            {
                yield return "--releaseNotes";
                yield return ReleaseNotes!;
            }

            if (!string.IsNullOrWhiteSpace(DeltaMode))
            {
                yield return "--delta";
                yield return DeltaMode!;
            }

            if (!string.IsNullOrWhiteSpace(Channel))
            {
                yield return "--channel";
                yield return Channel!;
            }

            if (!string.IsNullOrWhiteSpace(Exclude))
            {
                yield return "--exclude";
                yield return Exclude!;
            }

            if (!string.IsNullOrWhiteSpace(Runtimes))
            {
                yield return "--framework";
                yield return Runtimes!;
            }

            if (!string.IsNullOrWhiteSpace(SplashImage))
            {
                yield return "--splashImage";
                yield return SplashImage!;
            }

            if (!string.IsNullOrWhiteSpace(SignParameters))
            {
                yield return "--signParams";
                yield return SignParameters!;
            }

            if (!string.IsNullOrWhiteSpace(SignTemplate))
            {
                yield return "--signTemplate";
                yield return SignTemplate!;
            }

            if (!string.IsNullOrWhiteSpace(SignExclude))
            {
                yield return "--signExclude";
                yield return SignExclude!;
            }

            if (SignParallel != 10)
            {
                yield return "--signParallel";
                yield return SignParallel.ToString();
            }

            if (!string.IsNullOrWhiteSpace(Shortcuts))
            {
                yield return "--shortcuts";
                yield return Shortcuts!;
            }

            if (!string.IsNullOrWhiteSpace(Categories))
            {
                yield return "--categories";
                yield return Categories!;
            }

            if (!string.IsNullOrWhiteSpace(Compression))
            {
                yield return "--compression";
                yield return Compression!;
            }

            if (!string.IsNullOrWhiteSpace(SignAppIdentity))
            {
                yield return "--signAppIdentity";
                yield return SignAppIdentity!;
            }

            if (!string.IsNullOrWhiteSpace(SignInstallIdentity))
            {
                yield return "--signInstallIdentity";
                yield return SignInstallIdentity!;
            }

            if (!string.IsNullOrWhiteSpace(SignEntitlements))
            {
                yield return "--signEntitlements";
                yield return SignEntitlements!;
            }

            if (!string.IsNullOrWhiteSpace(NotaryProfile))
            {
                yield return "--notaryProfile";
                yield return NotaryProfile!;
            }

            if (!string.IsNullOrWhiteSpace(Keychain))
            {
                yield return "--keychain";
                yield return Keychain!;
            }

            if (!string.IsNullOrWhiteSpace(BundleId))
            {
                yield return "--bundleId";
                yield return BundleId!;
            }

            if (!string.IsNullOrWhiteSpace(InfoPlistPath))
            {
                yield return "--infoPlist";
                yield return InfoPlistPath!;
            }

            if (!string.IsNullOrWhiteSpace(AzureTrustedSignFile))
            {
                yield return "--azureTrustedSignFile";
                yield return AzureTrustedSignFile!;
            }

            if (!string.IsNullOrWhiteSpace(InstWelcome))
            {
                yield return "--instWelcome";
                yield return InstWelcome!;
            }

            if (!string.IsNullOrWhiteSpace(InstReadme))
            {
                yield return "--instReadme";
                yield return InstReadme!;
            }

            if (!string.IsNullOrWhiteSpace(InstLicense))
            {
                yield return "--instLicense";
                yield return InstLicense!;
            }

            if (!string.IsNullOrWhiteSpace(InstConclusion))
            {
                yield return "--instConclusion";
                yield return InstConclusion!;
            }

            if (!string.IsNullOrWhiteSpace(MsiVersionOverride))
            {
                yield return "--msiVersionOverride";
                yield return MsiVersionOverride!;
            }

            if (SkipVelopackAppCheck)
            {
                yield return "--skipVeloAppCheck";
            }

            if (NoPortable)
            {
                yield return "--noPortable";
            }

            if (NoInst)
            {
                yield return "--noInst";
            }

            if (BuildMsi)
            {
                yield return "--msi";
            }

            if (SignDisableDeep)
            {
                yield return "--signDisableDeep";
            }
        }

        return [..GetArguments()];
    }
}
