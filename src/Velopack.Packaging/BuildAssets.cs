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

        public static void Write(string outputDir, string channel, List<string> files)
        {
            var assets = new BuildAssets {
                Files = files,
            };
            var path = Path.Combine(outputDir, $"{channel}.assets.json");
            var json = JsonSerializer.Serialize(assets);
            File.WriteAllText(path, json);
        }

        public static BuildAssets Read(string outputDir, string channel)
        {
            var path = Path.Combine(outputDir, $"{channel}.assets.json");
            if (!File.Exists(path)) {
                throw new UserInfoException($"Could not find assets file for channel '{channel}' (looking for '{Path.GetFileName(path)}' in directory '{outputDir}')");
            }
            return JsonSerializer.Deserialize<BuildAssets>(File.ReadAllText(path));
        }
    }
}
