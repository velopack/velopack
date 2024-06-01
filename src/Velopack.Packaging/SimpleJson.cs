namespace Velopack.Json;

public class SimpleJson
{
#if NET6_0_OR_GREATER
    private static readonly System.Text.Json.JsonSerializerOptions Options = new System.Text.Json.JsonSerializerOptions {
        AllowTrailingCommas = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = {
            new System.Text.Json.Serialization.JsonStringEnumConverter(),
            new SemanticVersionConverter(),
        },
    };
#endif

    public static T DeserializeObject<T>(string json)
    {
#if NET6_0_OR_GREATER
        return System.Text.Json.JsonSerializer.Deserialize<T>(json, Options);
#else
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json, CompiledJson.Options);
#endif
    }

    public static string SerializeObject<T>(T obj)
    {
#if NET6_0_OR_GREATER
        return System.Text.Json.JsonSerializer.Serialize(obj, Options);
#else
        return Newtonsoft.Json.JsonConvert.SerializeObject(obj, CompiledJson.Options);
#endif
    }
}
