
#nullable enable

using NuGet.Versioning;

namespace Velopack.Packaging.Flow;

public class UploadOptions : VelopackServiceOptions
{
    public Stream ReleaseData { get; }
    public string FileName { get; }
    public string? Channel { get; }

    public UploadOptions(Stream releaseData, string fileName, string? channel)
    {
        ReleaseData = releaseData;
        FileName = fileName;

        Channel = channel;
    }
}
