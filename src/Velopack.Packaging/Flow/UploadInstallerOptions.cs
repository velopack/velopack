
#nullable enable

using NuGet.Versioning;

namespace Velopack.Packaging.Flow;

public class UploadInstallerOptions : UploadOptions
{
    public string PackageId { get; }

    public SemanticVersion Version { get; }

    public UploadInstallerOptions(string packageId, SemanticVersion version, Stream releaseData, string fileName, string? channel)
        : base(releaseData, fileName, channel)
    {
        PackageId = packageId;
        Version = version;
    }
}