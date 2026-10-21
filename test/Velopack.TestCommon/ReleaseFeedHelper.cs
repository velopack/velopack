using System.Text.Json.Nodes;

namespace Velopack.TestCommon;

/// <summary> Edits a local release feed (<c>releases.{channel}.json</c>) for tests. </summary>
public static class ReleaseFeedHelper
{
    /// <summary>
    /// Replaces the SHA1/SHA256 of every Full asset in <c>releases.{channel}.json</c> with zeros: a classic full download then
    /// fails its checksum, so a successful download proves the chunk path (verified by the index digest and per-chunk hashes)
    /// ran. Throws when the feed has no Full asset, or when <paramref name="requireChunkIndex"/> and a Full asset carries no
    /// chunk index pointer (the chunk path could not run, and the test would only fail later and less clearly).
    /// </summary>
    public static void CorruptFullAssetChecksums(string releaseDir, string channel = "win", bool requireChunkIndex = false)
    {
        var path = Path.Combine(releaseDir, $"releases.{channel}.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        int corrupted = 0;
        foreach (var asset in root["Assets"]!.AsArray()) {
            if (asset!["Type"]!.ToString() != "Full") continue;
            if (requireChunkIndex && string.IsNullOrEmpty(asset["ChunkIndexSHA256"]?.ToString())) {
                throw new InvalidOperationException($"The Full asset {asset["FileName"]} in {path} carries no chunk index pointer.");
            }
            asset["SHA1"] = new string('0', 40);
            asset["SHA256"] = new string('0', 64);
            corrupted++;
        }
        if (corrupted == 0) throw new InvalidOperationException("No Full assets found in " + path);
        File.WriteAllText(path, root.ToJsonString());
    }
}
