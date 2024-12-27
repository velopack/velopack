﻿using Newtonsoft.Json;
using Velopack.Util;

namespace Velopack.Core;

public class BuildAssets
{
    [JsonIgnore]
    private string? _outputDir;

    public List<string>? RelativeFileNames { get; set; } = [];

    public List<VelopackAsset> GetReleaseEntries()
    {
        return GetFilePaths()
            .Where(x => x.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            .Select(VelopackAsset.FromNupkg)
            .ToList();
    }

    public List<string> GetNonReleaseAssetPaths()
    {
        return GetFilePaths()
            .Where(x => !x.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IEnumerable<string> GetFilePaths()
    {
        if (RelativeFileNames is { } relativeFileNames && _outputDir is { } outputDir) {
            return relativeFileNames.Select(f => Path.GetFullPath(Path.Combine(outputDir, f))).ToList();
        }
        return [];
    }

    public static void Write(string outputDir, string channel, IEnumerable<string> files)
    {
        var assets = new BuildAssets {
            RelativeFileNames = files.OrderBy(f => f)
                .Select(f => PathUtil.MakePathRelativeTo(outputDir, f))
                .ToList(),
        };
        var path = Path.Combine(outputDir, $"assets.{channel}.json");
        var json = SimpleJson.SerializeObject(assets);
        File.WriteAllText(path, json);
    }

    public static BuildAssets Read(string outputDir, string channel)
    {
        var path = Path.Combine(outputDir, $"assets.{channel}.json");
        if (!File.Exists(path)) {
            throw new UserInfoException(
                $"Could not find assets file for channel '{channel}' (looking for '{Path.GetFileName(path)}' in directory '{outputDir}'). " +
                $"If you've just created a Velopack release, verify you're calling this command with the same '--channel' as you did with 'pack'.");
        }

        var assets = SimpleJson.DeserializeObject<BuildAssets>(File.ReadAllText(path)) ?? new();
        assets._outputDir = outputDir;
        return assets;
    }
}