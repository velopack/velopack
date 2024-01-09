namespace Velopack.Packaging.Unix.Commands;

public class OsxPackOptions : OsxBundleOptions, IPackOptions
{
    public RID TargetRuntime { get; set; }

    public string ReleaseNotes { get; set; }

    public DeltaMode DeltaMode { get; set; } = DeltaMode.BestSpeed;

    public bool NoPackage { get; set; }

    public string PackageWelcome { get; set; }

    public string PackageReadme { get; set; }

    public string PackageLicense { get; set; }

    public string PackageConclusion { get; set; }

    public string SigningAppIdentity { get; set; }

    public string SigningInstallIdentity { get; set; }

    public string SigningEntitlements { get; set; }

    public string NotaryProfile { get; set; }

    public string Channel { get; set; }
}
