using System.IO;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine
{
    internal abstract class BaseOptions
    {
        public string releaseDir { get; set; }

        protected static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(BaseOptions));

        public DirectoryInfo GetReleaseDirectory(bool createIfMissing = true)
        {
            var targetDir = Path.GetFullPath(releaseDir ?? Path.Combine(".", "releases"));
            var di = new DirectoryInfo(targetDir);
            if (!di.Exists && createIfMissing) di.Create();
            return di;
        }
    }

    internal class SyncS3Options : BaseOptions
    {
        public string keyId { get; set; }
        public string secret { get; set; }
        public string region { get; set; }
        public string endpoint { get; set; }
        public string bucket { get; set; }
        public string pathPrefix { get; set; }
        public bool overwrite { get; set; }
        public int keepMaxReleases { get; set; }
    }

    internal class SyncHttpOptions : BaseOptions
    {
        public string url { get; set; }
    }

    internal class SyncGithubOptions : BaseOptions
    {
        public string repoUrl { get; set; }
        public string token { get; set; }
        public bool pre { get; set; }
        public bool publish { get; set; }
        public string releaseName { get; set; }
    }
}