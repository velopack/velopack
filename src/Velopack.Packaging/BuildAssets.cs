using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Velopack.Packaging.Exceptions;

namespace Velopack.Packaging
{
    public class BuildAssets
    {
        public List<string> Files { get; set; } = new List<string>();

        public List<VelopackAsset> GetReleaseEntries()
        {
            return Files.Where(x => x.EndsWith(".nupkg"))
                .Select(f => VelopackAsset.FromNupkg(f))
                .ToList();
        }

        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions {
            AllowTrailingCommas = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        public static void Write(string outputDir, string channel, IEnumerable<string> files)
        {
            var assets = new BuildAssets {
                Files = files.OrderBy(f => f).ToList(),
            };
            var path = Path.Combine(outputDir, $"assets.{channel}.json");
            var json = JsonSerializer.Serialize(assets, _options);
            File.WriteAllText(path, json);
        }

        public static BuildAssets Read(string outputDir, string channel)
        {
            var path = Path.Combine(outputDir, $"assets.{channel}.json");
            if (!File.Exists(path)) {
                throw new UserInfoException($"Could not find assets file for channel '{channel}' (looking for '{Path.GetFileName(path)}' in directory '{outputDir}'). " +
                    $"If you've just created a Velopack release, verify you're calling this command with the same '--channel' as you did with 'pack'.");
            }
            return JsonSerializer.Deserialize<BuildAssets>(File.ReadAllText(path), _options);
        }
    }
}
