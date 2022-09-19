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

        //public SyncHttpOptions()
        //{
        //    Add("url=", "Base url to the http location with hosted releases", v => url = v);
        //}

        //public override void Validate()
        //{
        //    IsRequired(nameof(url));
        //    IsValidUrl(nameof(url));
        //}
    }

    internal class SyncGithubOptions : BaseOptions
    {
        public string repoUrl { get; private set; }
        public string token { get; private set; }
        public bool pre { get; private set; }
        public bool publish { get; private set; }
        public string releaseName { get; private set; }

        //public SyncGithubOptions()
        //{
        //    Add("repoUrl=", "Full url to the github repository\nexample: 'https://github.com/myname/myrepo'", v => repoUrl = v);
        //    Add("token=", "OAuth token to use as login credentials", v => token = v);
        //    Add("pre", "(down only) Get latest pre-release instead of stable", v => pre = true);
        //    Add("publish", "(up only) Publish release instead of creating draft", v => publish = true);
        //    Add("releaseName=", "(up only) A custom {NAME} for created release", v => releaseName = v);
        //}

        //public override void Validate()
        //{
        //    IsRequired(nameof(repoUrl));
        //    IsValidUrl(nameof(repoUrl));
        //}
    }
}