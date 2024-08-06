#nullable enable

namespace Velopack.Packaging.Flow;

public class UploadOptions(Stream releaseData, string fileName, string channel) : VelopackServiceOptions
{
    public Stream ReleaseData { get; } = releaseData;
    public string FileName { get; } = fileName;
    public string Channel { get; } = channel;
}
