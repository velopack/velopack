using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Velopack.Core.Json;

/// <summary>
/// Overlays values from a JSON config file onto an existing options object. Only properties
/// present in the JSON are modified, so any values already set (eg. from environment variables
/// or defaults) are preserved when omitted from the JSON. Keys are matched to the property
/// names of the options class (case-insensitive, camelCase recommended).
/// </summary>
public static class JsonConfigLoader
{
    public static void Populate(string path, object target)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            throw new UserInfoException($"JSON config file does not exist: '{path}'.");
        }

        string json;
        try {
            json = File.ReadAllText(path);
        } catch (Exception ex) {
            throw new UserInfoException($"Failed to read JSON config file '{path}': {ex.Message}");
        }

        PopulateFromJson(json, target);
    }

    public static void PopulateFromJson(string json, object target)
    {
        try {
            // DateParseHandling.None stops Json.NET from re-interpreting date-like string values,
            // which would otherwise be mangled (eg. timezone shifted) before reaching a string property.
            JObject obj;
            using (var stringReader = new StringReader(json))
            using (var jsonReader = new JsonTextReader(stringReader) { DateParseHandling = DateParseHandling.None }) {
                obj = JObject.Load(jsonReader);
            }

            obj.Remove("$schema");

            var settings = new JsonSerializerSettings {
                // error on unknown keys so typos are caught instead of silently ignored
                MissingMemberHandling = MissingMemberHandling.Error,
                DateParseHandling = DateParseHandling.None,
                Converters = {
                    new StringEnumConverter(),
                    new FileInfoJsonConverter(),
                    new DirectoryInfoJsonConverter(),
                    new RidJsonConverter(),
                },
            };

            using var objReader = obj.CreateReader();
            JsonSerializer.Create(settings).Populate(objReader, target);
        } catch (UserInfoException) {
            throw;
        } catch (JsonException ex) {
            throw new UserInfoException("Invalid JSON config: " + ex.Message);
        }
    }
}

public class FileInfoJsonConverter : JsonConverter<FileInfo?>
{
    public override FileInfo? ReadJson(JsonReader reader, Type objectType, FileInfo? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        if (reader.Value is not string s) {
            throw new JsonSerializationException($"Expected a string value for {reader.Path}.");
        }

        try {
            return new FileInfo(s);
        } catch (Exception ex) {
            throw new JsonSerializationException($"Invalid file path '{s}' for {reader.Path}: {ex.Message}");
        }
    }

    public override void WriteJson(JsonWriter writer, FileInfo? value, JsonSerializer serializer)
    {
        writer.WriteValue(value?.FullName);
    }
}

public class DirectoryInfoJsonConverter : JsonConverter<DirectoryInfo?>
{
    public override DirectoryInfo? ReadJson(JsonReader reader, Type objectType, DirectoryInfo? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        if (reader.Value is not string s) {
            throw new JsonSerializationException($"Expected a string value for {reader.Path}.");
        }

        try {
            return new DirectoryInfo(s);
        } catch (Exception ex) {
            throw new JsonSerializationException($"Invalid directory path '{s}' for {reader.Path}: {ex.Message}");
        }
    }

    public override void WriteJson(JsonWriter writer, DirectoryInfo? value, JsonSerializer serializer)
    {
        writer.WriteValue(value?.FullName);
    }
}

public class RidJsonConverter : JsonConverter<RID?>
{
    public override RID? ReadJson(JsonReader reader, Type objectType, RID? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        if (reader.Value is not string s) {
            throw new JsonSerializationException($"Expected a string value for {reader.Path}.");
        }

        try {
            return RID.Parse(s);
        } catch (Exception ex) {
            throw new JsonSerializationException($"Invalid runtime identifier '{s}': {ex.Message}");
        }
    }

    public override void WriteJson(JsonWriter writer, RID? value, JsonSerializer serializer)
    {
        writer.WriteValue(value?.ToString());
    }
}
