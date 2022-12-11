using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Squirrel.CommandLine.Commands;

namespace Squirrel.CommandLine.Sync
{
    internal class SimpleWebRepository
    {
        private readonly HttpDownloadCommand options;

        public SimpleWebRepository(HttpDownloadCommand options)
        {
            this.options = options;
        }

        public static async Task DownloadRecentPackages(HttpDownloadCommand options)
        {
            var uri = new Uri(options.Url);
            var releasesDir = options.GetReleaseDirectory();
            var releasesUri = Utility.AppendPathToUri(uri, "RELEASES");
            var releasesIndex = await retryAsync(3, () => downloadReleasesIndex(releasesUri));

            File.WriteAllText(Path.Combine(releasesDir.FullName, "RELEASES"), releasesIndex);

            var releasesToDownload = ReleaseEntry.ParseReleaseFile(releasesIndex)
                .Where(x => !x.IsDelta)
                .OrderByDescending(x => x.Version)
                .Take(1)
                .Select(x => new {
                    LocalPath = Path.Combine(releasesDir.FullName, x.Filename),
                    RemoteUrl = new Uri(Utility.EnsureTrailingSlash(uri), x.BaseUrl + x.Filename + x.Query)
                });

            foreach (var releaseToDownload in releasesToDownload) {
                await retryAsync(3, () => downloadRelease(releaseToDownload.LocalPath, releaseToDownload.RemoteUrl));
            }
        }

        static async Task<string> downloadReleasesIndex(Uri uri)
        {
            Console.WriteLine("Trying to download RELEASES index from {0}", uri);

            var userAgent = new System.Net.Http.Headers.ProductInfoHeaderValue("Squirrel", Assembly.GetExecutingAssembly().GetName().Version.ToString());
            using (HttpClient client = new HttpClient()) {
                client.DefaultRequestHeaders.UserAgent.Add(userAgent);
                return await client.GetStringAsync(uri);
            }
        }

        static async Task downloadRelease(string localPath, Uri remoteUrl)
        {
            if (File.Exists(localPath)) {
                File.Delete(localPath);
            }

            Console.WriteLine("Downloading release from {0}", remoteUrl);
            var wc = Utility.CreateDefaultDownloader();
            await wc.DownloadFile(remoteUrl.ToString(), localPath, null);
        }

        static async Task<T> retryAsync<T>(int count, Func<Task<T>> block)
        {
            int retryCount = count;

        retry:
            try {
                return await block();
            } catch (Exception) {
                retryCount--;
                if (retryCount >= 0) goto retry;

                throw;
            }
        }

        static async Task retryAsync(int count, Func<Task> block)
        {
            await retryAsync(count, async () => { await block(); return false; });
        }
    }
}
