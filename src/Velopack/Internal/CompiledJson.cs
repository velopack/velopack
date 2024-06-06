using System;
using NuGet.Versioning;
using Velopack.Sources;
using System.Collections.Generic;

#if NET6_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
#else
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
#endif

#if !NET6_0_OR_GREATER
namespace System.Text.Json.Serialization
{
    // this is just here so our code can "use" System.Text.Json.Serialization
    // without having conditional compilation everywhere
    internal class JsonPlaceholderNoopDontUse { }
}
#endif

namespace Velopack.Json
{
#if NET6_0_OR_GREATER

    [JsonSerializable(typeof(List<GithubRelease>))]
    [JsonSerializable(typeof(List<GitlabRelease>))]
    [JsonSerializable(typeof(List<GiteaRelease>))]
    [JsonSerializable(typeof(VelopackAssetFeed))]
#if NET8_0_OR_GREATER
    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
#endif
    internal partial class CompiledJsonSourceGenerationContext : JsonSerializerContext
    {
    }

    internal static class CompiledJson
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = {
#if !NET8_0_OR_GREATER
                new JsonStringEnumConverter(),
#endif
                new SemanticVersionConverter(),
            },
        };

        private static readonly CompiledJsonSourceGenerationContext Context = new CompiledJsonSourceGenerationContext(Options);

        public static List<GithubRelease>? DeserializeGithubReleaseList(string json)
        {
            return JsonSerializer.Deserialize(json, Context.ListGithubRelease);
        }

        public static List<GiteaRelease>? DeserializeGiteaReleaseList(string json)
        {
            return JsonSerializer.Deserialize(json, Context.ListGiteaRelease);
        }

        public static List<GitlabRelease>? DeserializeGitlabReleaseList(string json)
        {
            return JsonSerializer.Deserialize(json, Context.ListGitlabRelease);
        }

        public static VelopackAsset[]? DeserializeVelopackAssetArray(string json)
        {
            return JsonSerializer.Deserialize(json, Context.VelopackAssetArray);
        }

        public static VelopackAssetFeed? DeserializeVelopackAssetFeed(string json)
        {
            return JsonSerializer.Deserialize(json, Context.VelopackAssetFeed);
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

    internal static class CompiledJson
    {
        public static readonly JsonSerializerSettings Options = new JsonSerializerSettings {
            Converters = { new StringEnumConverter(), new SemanticVersionConverter() },
            ContractResolver = new JsonNameContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static List<GithubRelease>? DeserializeGithubReleaseList(string json)
        {
            return JsonConvert.DeserializeObject<List<GithubRelease>>(json, Options);
        }

        public static List<GiteaRelease>? DeserializeGiteaReleaseList(string json)
        {
            return JsonConvert.DeserializeObject<List<GiteaRelease>>(json, Options);
        }

        public static List<GitlabRelease>? DeserializeGitlabReleaseList(string json)
        {
            return JsonConvert.DeserializeObject<List<GitlabRelease>>(json, Options);
        }

        public static VelopackAsset[]? DeserializeVelopackAssetArray(string json)
        {
            return JsonConvert.DeserializeObject<VelopackAsset[]>(json, Options);
        }

        public static VelopackAssetFeed? DeserializeVelopackAssetFeed(string json)
        {
            return JsonConvert.DeserializeObject<VelopackAssetFeed>(json, Options);
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