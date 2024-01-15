using System;
using NuGet.Versioning;
using System.Text.Json.Serialization;

#if NET5_0_OR_GREATER
using System.Text.Json;
#else
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
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
            Converters = { new JsonStringEnumConverter(), new SemanticVersionConverter() },
        };

        public static T DeserializeObject<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
    }

    internal class SemanticVersionConverter : JsonConverter<SemanticVersion>
    {
        public override SemanticVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return SemanticVersion.Parse(reader.GetString());
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
        public static T DeserializeObject<T>(string json)
        {
            var options = new JsonSerializerSettings {
                Converters = { new StringEnumConverter(), new SemanticVersionConverter() },
                ContractResolver = new JsonNameContractResolver(),
            };
            return JsonConvert.DeserializeObject<T>(json, options);
        }
    }

    internal class SemanticVersionConverter : JsonConverter<SemanticVersion>
    {
        public override SemanticVersion ReadJson(JsonReader reader, Type objectType, SemanticVersion existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            string s = (string) reader.Value;
            return SemanticVersion.Parse(s);
        }

        public override void WriteJson(JsonWriter writer, SemanticVersion value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToFullString());
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