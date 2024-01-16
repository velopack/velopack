using System;
using NuGet.Versioning;

#if NET5_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
#else
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
#endif

#if !NET5_0_OR_GREATER
namespace System.Text.Json.Serialization
{
    // this is just here so our code can "use" System.Text.Json.Serialization
    // without having conditional compilation everywhere
    internal class JsonPlaceholderNoopDontUse { }
}
#endif

namespace Velopack.Json
{
#if NET5_0_OR_GREATER
    internal static class SimpleJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(), new SemanticVersionConverter() },
        };

        public static T? DeserializeObject<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }

        public static string SerializeObject<T>(T obj)
        {
            return JsonSerializer.Serialize(obj, Options);
        }
    }

    internal class SemanticVersionConverter : JsonConverter<SemanticVersion>
    {
        public override SemanticVersion? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var str = reader.GetString();
            if (str == null) return null;
            return SemanticVersion.Parse(str);
        }

        public override void Write(Utf8JsonWriter writer, SemanticVersion value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToFullString());
        }
    }
#else
    internal class JsonPropertyNameAttribute : Attribute
    {
        public string Name { get; }

        public JsonPropertyNameAttribute(string name)
        {
            Name = name;
        }
    }

    internal static class SimpleJson
    {
        private static readonly JsonSerializerSettings Options = new JsonSerializerSettings {
            Converters = { new StringEnumConverter(), new SemanticVersionConverter() },
            ContractResolver = new JsonNameContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static T? DeserializeObject<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Options);
        }

        public static string SerializeObject<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented, Options);
        }
    }

    internal class SemanticVersionConverter : JsonConverter<SemanticVersion>
    {
        public override SemanticVersion? ReadJson(JsonReader reader, Type objectType, SemanticVersion? existingValue, bool hasExistingValue, JsonSerializer serializer)
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

    internal class JsonNameContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);
            if (member.GetCustomAttribute<JsonPropertyNameAttribute>() is { } stj) {
                property.PropertyName = stj.Name;
                return property;
            }
            return property;
        }
    }
#endif
}