#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0612 // Type or member is obsolete
using System.Text;
using System.Text.Json;
using Velopack.Sources;

namespace Velopack.Tests.TestHelpers
{
    internal class FakeFixtureRepository : Sources.IFileDownloader
    {
        private readonly string _pkgId;
        private readonly IEnumerable<ReleaseEntry> _releases;
        private readonly VelopackAssetFeed _releasesNew;
        private readonly string _releasesName;
        private readonly string _releasesNameNew;

        public FakeFixtureRepository(string pkgId, bool mockLatestFullVer, string channel = null)
        {
            _releasesName = Utility.GetReleasesFileName(channel);
            _releasesNameNew = Utility.GetVeloReleaseIndexName(channel);
            _pkgId = pkgId;
            var releases = ReleaseEntry.BuildReleasesFile(PathHelper.GetFixturesDir(), false)
                .Where(r => r.OriginalFilename.StartsWith(_pkgId))
                .ToList();

            var releasesNew = new SimpleFileSource(new DirectoryInfo(PathHelper.GetFixturesDir()))
                .GetReleaseFeed(NullLogger.Instance, null).GetAwaiterResult().Assets
                .Where(r => r.FileName.StartsWith(_pkgId))
                .ToList();

            if (mockLatestFullVer) {
                var minFullVer = releases.Where(r => !r.IsDelta).OrderBy(r => r.Version).First();
                var maxfullVer = releases.Where(r => !r.IsDelta).OrderByDescending(r => r.Version).First();
                var maxDeltaVer = releases.Where(r => r.IsDelta).OrderByDescending(r => r.Version).First();

                // our fixtures don't have a full package for the latest version, we expect the tests to generate this file
                if (maxfullVer.Version < maxDeltaVer.Version) {
                    var name = new ReleaseEntryName(maxfullVer.PackageId, maxDeltaVer.Version, false);
                    releases.Add(new ReleaseEntry("0000000000000000000000000000000000000000", name.ToFileName(), maxfullVer.Filesize));

                    releasesNew.Add(new VelopackAsset {
                        PackageId = maxfullVer.PackageId,
                        Version = maxDeltaVer.Version,
                        Type = VelopackAssetType.Full,
                        FileName = $"{maxfullVer.PackageId}-{maxDeltaVer.Version}-full.nupkg",
                        Size = maxfullVer.Filesize,
                    });
                }
            }

            _releasesNew = new VelopackAssetFeed {
                Assets = releasesNew.ToArray(),
            };
            _releases = releases;
        }

        public Task<byte[]> DownloadBytes(string url, string authorization = null, string accept = null)
        {
            if (url.Contains($"/{_releasesName}?")) {
                MemoryStream ms = new MemoryStream();
                ReleaseEntry.WriteReleaseFile(_releases, ms);
                return Task.FromResult(ms.ToArray());
            }

            if (url.Contains($"/{_releasesNameNew}?")) {
                var json = JsonSerializer.Serialize(_releasesNew, SimpleJsonTests.Options);
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

        public Task DownloadFile(string url, string targetFile, Action<int> progress, string authorization = null, string accept = null, CancellationToken token = default)
        {
            var rel = _releases.FirstOrDefault(r => url.EndsWith(r.OriginalFilename));
            var filePath = PathHelper.GetFixture(rel.OriginalFilename);
            if (!File.Exists(filePath)) {
                throw new NotSupportedException("FakeFixtureRepository doesn't have: " + rel.OriginalFilename);
            }

            File.Copy(filePath, targetFile);
            progress(25);
            progress(50);
            progress(75);
            progress(100);
            return Task.CompletedTask;
        }

        public Task<string> DownloadString(string url, string authorization = null, string accept = null)
        {
            if (url.Contains($"/{_releasesName}?")) {
                MemoryStream ms = new MemoryStream();
                ReleaseEntry.WriteReleaseFile(_releases, ms);
                return Task.FromResult(Encoding.UTF8.GetString(ms.ToArray()));
            }

            if (url.Contains($"/{_releasesNameNew}?")) {
                var json = JsonSerializer.Serialize(_releasesNew, SimpleJsonTests.Options);
                return Task.FromResult(json);
            }

            throw new NotSupportedException("FakeFixtureRepository doesn't have: " + url);
        }
    }
}
