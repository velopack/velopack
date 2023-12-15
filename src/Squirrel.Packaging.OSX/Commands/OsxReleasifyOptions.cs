namespace Squirrel.Packaging.OSX.Commands;

public class OsxReleasifyOptions
{
    public DirectoryInfo ReleaseDir { get; set; }

    public RID TargetRuntime { get; set; }

    public string BundleDirectory { get; set; }

    public bool IncludePdb { get; set; }

    public string ReleaseNotes { get; set; }

    public bool NoDelta { get; set; }

    public bool NoPackage { get; set; }

    public string PackageWelcome { get; set; }

    public string PackageReadme { get; set; }

    public string PackageLicense { get; set; }

    public string PackageConclusion { get; set; }

    public string SigningAppIdentity { get; set; }

    public string SigningInstallIdentity { get; set; }

    public string SigningEntitlements { get; set; }

    public string NotaryProfile { get; set; }
}
