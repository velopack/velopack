using Velopack.Packaging.Exceptions;

namespace Velopack.Packaging;

public class BuildAssets
{
    public List<string> Files { get; set; } = new List<string>();

    public List<VelopackAsset> GetReleaseEntries()
    {
        return Files.Where(x => x.EndsWith(".nupkg"))
            .Select(f => VelopackAsset.FromNupkg(f))
            .ToList();
    }

    public static void Write(string outputDir, string channel, IEnumerable<string> files)
    {
        var assets = new BuildAssets {
            Files = files.OrderBy(f => f).ToList(),
        };
        var path = Path.Combine(outputDir, $"assets.{channel}.json");
        var json = SimpleJson.SerializeObject(assets);
        File.WriteAllText(path, json);
    }

    public static BuildAssets Read(string outputDir, string channel)
    {
        var path = Path.Combine(outputDir, $"assets.{channel}.json");
        if (!File.Exists(path)) {
            throw new UserInfoException($"Could not find assets file for channel '{channel}' (looking for '{Path.GetFileName(path)}' in directory '{outputDir}'). " +
                $"If you've just created a Velopack release, verify you're calling this command with the same '--channel' as you did with 'pack'.");
        }
        return SimpleJson.DeserializeObject<BuildAssets>(File.ReadAllText(path));
    }
}
