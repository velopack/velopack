using NuGet.Versioning;
using Velopack.NuGet;

namespace Velopack.Packaging;

public class ReleasePackage
{
    private Lazy<ZipPackage> _package;

    public ReleasePackage(string inputPackageFile)
    {
        PackageFile = inputPackageFile;
        _package = new Lazy<ZipPackage>(() => new ZipPackage(inputPackageFile));
    }

    public string PackageFile { get; protected set; }

    public SemanticVersion Version => _package.Value.Version;
}