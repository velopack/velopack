using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NuGet.Versioning;

namespace Velopack.Core;

public class SimpleJson
{
    private static readonly JsonSerializerSettings Options = new JsonSerializerSettings {
        Converters = { new StringEnumConverter(), new SemanticVersionConverter() },
        NullValueHandling = NullValueHandling.Ignore,
    };

    public static T? DeserializeObject<T>(string json)
    {
        return JsonConvert.DeserializeObject<T>(json, Options);
    }

    public static string SerializeObject<T>(T obj)
    {
        return JsonConvert.SerializeObject(obj, Options);
    }

    private class SemanticVersionConverter : JsonConverter<SemanticVersion>
    {
        public override SemanticVersion? ReadJson(JsonReader reader, Type objectType, SemanticVersion? existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            string? s = reader.Value as string;
            if (s == null) return null;
            return SemanticVersion.Parse(s);
        }

        public override void WriteJson(JsonWriter writer, SemanticVersion? value, JsonSerializer serializer)
        {
            if (value != null) {
                writer.WriteValue(value.ToFullString());
            } else {
                writer.WriteNull();
            }
        }
    }
}