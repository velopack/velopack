using Microsoft.Extensions.Logging;
using Velopack.NuGet;
using Velopack.Packaging;
using Velopack.Sources;

namespace Velopack.Deployment;


public class VelopackDownloadOptions : RepositoryOptions
{
    public string Version { get; set; }
}

public class VelopackUploadOptions : VelopackDownloadOptions
{
}

public class VelopackRepository : SourceRepository<VelopackDownloadOptions, VelopackFlowUpdateSource>, IRepositoryCanUpload<VelopackUploadOptions>
{
    private static HttpClient Client { get; } = new HttpClient {
        BaseAddress = new Uri("https://velopack.keboo.dev/")
    };

    public VelopackRepository(ILogger logger)
        : base(logger)
    { }

    public override VelopackFlowUpdateSource CreateSource(VelopackDownloadOptions options)
    {
        return new VelopackFlowUpdateSource();
    }

    public async Task UploadMissingAssetsAsync(VelopackUploadOptions options)
    {
        var helper = new ReleaseEntryHelper(options.ReleaseDir.FullName, options.Channel, Log);
        var latest = helper.GetLatestAssets().ToList();

        Log.Info($"Preparing to upload {latest.Count} assets to Velopack");

        foreach (var asset in latest) {
            
            var latestPath = Path.Combine(options.ReleaseDir.FullName, asset.FileName);
            ZipPackage zipPackage = new(latestPath);
            var semVer = options.Version ?? asset.Version.ToString();

            using var formData = new MultipartFormDataContent
            {
                { new StringContent(options.Channel), "Channel" },
            };

            using var fileStream = File.OpenRead(latestPath);
            using var fileContent = new StreamContent(fileStream);
            formData.Add(fileContent, "File", asset.FileName);

            var response = await Client.PostAsync("api/v1/upload", formData);
            response.EnsureSuccessStatusCode();

            Log.Info($"    Uploaded {asset.FileName} to Velopack");
        }
    }
}