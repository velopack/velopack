using System.IO.Compression;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.Compression;
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
