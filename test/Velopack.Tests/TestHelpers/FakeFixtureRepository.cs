#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0612 // Type or member is obsolete
using System.Text;
using Velopack.Core;
using Velopack.Logging;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Tests.TestHelpers;

internal class FakeFixtureRepository : IFileDownloader
{
    private readonly string _pkgId;
    private readonly IEnumerable<ReleaseEntry> _releases;
    private readonly VelopackAssetFeed _releasesNew;
    private readonly string _releasesName;
    private readonly string _releasesNameNew;

    public FakeFixtureRepository(string pkgId, string channel = null)
    {
        _releasesName = CoreUtil.GetReleasesFileName(channel);
        _releasesNameNew = CoreUtil.GetVeloReleaseIndexName(channel);
        _pkgId = pkgId;
        var releases = ReleaseEntry.BuildReleasesFile(PathHelper.GetFixturesDir(), false)
            .Where(r => r.OriginalFilename.StartsWith(_pkgId))
            .ToList();

        var releasesNew = new SimpleFileSource(new DirectoryInfo(PathHelper.GetFixturesDir()))
            .GetReleaseFeed(NullVelopackLogger.Instance, null, null).GetAwaiterResult().Assets
            .Where(r => r.FileName.StartsWith(_pkgId))
            .ToList();

        _releasesNew = new VelopackAssetFeed {
            Assets = releasesNew.ToArray(),
        };
        _releases = releases;
    }

    public Task<byte[]> DownloadBytes(string url, IDictionary<string, string> headers = null, double timeout = 30)
    {
        if (url.Contains($"/{_releasesName}?")) {
            MemoryStream ms = new MemoryStream();
            ReleaseEntry.WriteReleaseFile(_releases, ms);
            return Task.FromResult(ms.ToArray());
        }

        if (url.Contains($"/{_releasesNameNew}?")) {
            var json = SimpleJson.SerializeObject(_releasesNew);
            return Task.FromResult(Encoding.UTF8.GetBytes(json));
        }

        var rel = _releases.FirstOrDefault(r => url.EndsWith(r.OriginalFilename));
        if (rel == null)
            throw new Exception("Fake release not found: " + url);

        var filePath = PathHelper.GetFixture(rel.OriginalFilename);
        if (!File.Exists(filePath)) {
            throw new NotSupportedException("FakeFixtureRepository doesn't have: " + rel.OriginalFilename);
        }

        return Task.FromResult(File.ReadAllBytes(filePath));
    }

    public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string> headers = null, double timeout = 30,
        CancellationToken token = default)
    {
        var rel = _releases.FirstOrDefault(r => url.EndsWith(r.OriginalFilename));
        var filePath = PathHelper.GetFixture(rel.OriginalFilename);
        if (!File.Exists(filePath)) {
            throw new NotSupportedException("FakeFixtureRepository doesn't have: " + rel.OriginalFilename);
        }

        File.Copy(filePath, targetFile);
        // fsync to ensure data is flushed to disk before UpdateMac reads the file in a separate process.
        using (var fs = new FileStream(targetFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            fs.Flush(true);
        }
        progress(25);
        progress(50);
        progress(75);
        progress(100);
        return Task.CompletedTask;
    }

    public Task<string> DownloadString(string url, IDictionary<string, string> headers = null, double timeout = 30)
    {
        if (url.Contains($"/{_releasesName}?")) {
            MemoryStream ms = new MemoryStream();
            ReleaseEntry.WriteReleaseFile(_releases, ms);
            return Task.FromResult(Encoding.UTF8.GetString(ms.ToArray()));
        }

        if (url.Contains($"/{_releasesNameNew}?")) {
            var json = SimpleJson.SerializeObject(_releasesNew);
            return Task.FromResult(json);
        }

        throw new NotSupportedException("FakeFixtureRepository doesn't have: " + url);
    }
}