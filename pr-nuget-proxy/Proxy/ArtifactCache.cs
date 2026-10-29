namespace PrNuGetProxy;

public sealed class ArtifactCache(ProxyOptions options, IGitHubClient github)
{
    private readonly SemaphoreSlim _gate = new(1);

    public bool Contains(Artifact artifact) => File.Exists(GetPath(artifact));

    private string GetPath(Artifact artifact) => Path.Combine(options.CacheDirectory, "artifacts", $"{artifact.Id}.zip");

    public async Task<string> GetAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(options.CacheDirectory, "artifacts");
        var path = GetPath(artifact);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                await PackageRewriter.VerifyDigestAsync(path, artifact, cancellationToken);
                return path;
            }
            if (artifact.Expired) throw new IOException($"Artifact {artifact.Id} has expired and is not cached locally.");
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $"{artifact.Id}.{Guid.NewGuid():N}.tmp");
            try
            {
                await github.DownloadAsync(artifact, temporary, cancellationToken);
                await PackageRewriter.VerifyDigestAsync(temporary, artifact, cancellationToken);
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return path;
        }
        finally
        {
            _gate.Release();
        }
    }
}
