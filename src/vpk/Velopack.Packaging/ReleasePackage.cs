using NuGet.Versioning;
using Velopack.NuGet;

namespace Velopack.Packaging;

public class ReleasePackage
{
    private Lazy<PackageManifest> _package;

    public ReleasePackage(string inputPackageFile)
    {
        PackageFile = inputPackageFile;
        _package = new Lazy<PackageManifest>(() => ZipPackage.ReadManifest(inputPackageFile));
    }

    public string PackageFile { get; protected set; }

    public SemanticVersion Version => _package.Value.Version;
}
