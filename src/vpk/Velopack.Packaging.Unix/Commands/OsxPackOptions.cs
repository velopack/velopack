using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Compression;

namespace Velopack.Packaging.Unix.Commands;

public class OsxPackOptions : OsxBundleOptions, IPackOptions
{
    public RID TargetRuntime { get; set; }

    public string ReleaseNotes { get; set; }

    public DeltaMode DeltaMode { get; set; } = DeltaMode.BestSpeed;

    private bool _noInst;

    /// <summary>
    /// Skip the .pkg installer. Always true off macOS, where pkgbuild and productbuild do not exist.
    /// </summary>
    public bool NoInst {
        get => _noInst || !VelopackRuntimeInfo.IsOSX;
        set => _noInst = value;
    }

    public bool NoPortable { get; set; }

    public string InstWelcome { get; set; }

    public string InstReadme { get; set; }

    public string InstLicense { get; set; }

    public string InstConclusion { get; set; }

    public string SignAppIdentity { get; set; }

    public string SignInstallIdentity { get; set; }

    public string SignEntitlements { get; set; }
    
    public bool SignDisableDeep { get; set; }

    public string NotaryProfile { get; set; }

    public string Keychain { get; set; }

    public string SignP12File { get; set; }

    public string SignP12PasswordFile { get; set; }

    public string NotaryApiKeyFile { get; set; }

    public string Channel { get; set; }

    public string Exclude { get; set; } = @".*\.pdb";

    public bool NoDefaultExclude { get; set; }
}
