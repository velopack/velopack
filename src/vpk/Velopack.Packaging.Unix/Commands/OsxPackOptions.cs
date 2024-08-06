using Velopack.Packaging.Abstractions;

namespace Velopack.Packaging.Unix.Commands;

public class OsxPackOptions : OsxBundleOptions, IPackOptions
{
    public RID TargetRuntime { get; set; }

    public string ReleaseNotes { get; set; }

    public DeltaMode DeltaMode { get; set; } = DeltaMode.BestSpeed;

    public bool NoInst { get; set; }

    public bool NoPortable { get; set; }

    public string InstWelcome { get; set; }

    public string InstReadme { get; set; }

    public string InstLicense { get; set; }

    public string InstConclusion { get; set; }

    public string SignAppIdentity { get; set; }

    public string SignInstallIdentity { get; set; }

    public string SignEntitlements { get; set; }

    public string NotaryProfile { get; set; }

    public string Keychain { get; set; }

    public string Channel { get; set; }

    public string Exclude { get; set; }
}
