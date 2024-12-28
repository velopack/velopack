using System.Collections.Concurrent;
using Velopack.Util;

namespace Velopack.Core;

public class BuildAssets(string outputDir, string channel)
{
    public int Count => Assets.Count;

    class Asset
    {
        public string RelativeFileName { get; set; } = string.Empty;
        public VelopackAssetType Type { get; set; }
    }

    private ConcurrentBag<Asset> Assets { get; set; } = [];

    public List<VelopackAsset> GetReleaseEntries()
    {
        return GetAssets()
            .Where(x => x.Type is VelopackAssetType.Delta or VelopackAssetType.Full)
            .Select(x => VelopackAsset.FromNupkg(x.Path))
            .ToList();
    }

    public IEnumerable<(string Path, VelopackAssetType Type)> GetAssets()
    {
        return Assets.Select(asset => (Path.Combine(outputDir, asset.RelativeFileName), asset.Type));
    }

    public IEnumerable<string> GetFilePaths()
    {
        return Assets.Select(x => Path.Combine(outputDir, x.RelativeFileName));
    }

    public string MakeAssetPath(string relativePath, VelopackAssetType type)
    {
        // var relativeFileName = PathUtil.MakePathRelativeTo(outputDir, fullPath);
        Assets.Add(new Asset { RelativeFileName = relativePath, Type = type });
        return Path.Combine(outputDir, relativePath);
    }

    public void MoveBagTo(string newOutputDir)
    {
        foreach (var asset in Assets) {
            var from = Path.Combine(outputDir, asset.RelativeFileName);
            var to = Path.Combine(newOutputDir, asset.RelativeFileName);
            IoUtil.MoveFile(from, to, true);
        }

        outputDir = newOutputDir;
    }

    public void Write()
    {
        var path = Path.Combine(outputDir, $"assets.{channel}.json");
        var json = SimpleJson.SerializeObject(Assets);
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

        var me = new BuildAssets(outputDir, channel) {
            Assets = SimpleJson.DeserializeObject<ConcurrentBag<Asset>>(File.ReadAllText(path)) ?? []
        };
        return me;
    }
}