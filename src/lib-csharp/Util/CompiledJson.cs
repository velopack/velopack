using System;
using System.Collections.Generic;
using Velopack.Ipc;
using Velopack.Sources;

#if NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
#else
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
#endif

namespace Velopack.Util
{
#if NET8_0_OR_GREATER
    [JsonSerializable(typeof(List<GithubRelease>))]
    [JsonSerializable(typeof(List<GitlabRelease>))]
    [JsonSerializable(typeof(List<GiteaRelease>))]
    [JsonSerializable(typeof(VelopackAssetFeed))]
    [JsonSerializable(typeof(VelopackFlowReleaseAsset[]))]
    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
    internal partial class CompiledJsonSourceGenerationContext : JsonSerializerContext
    {
    }

    /// <summary> The IPC wire format: compact, camelCase, nulls omitted, strict (no comments or trailing commas). </summary>
    [JsonSerializable(typeof(IpcMessage))]
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    internal partial class IpcJsonSourceGenerationContext : JsonSerializerContext
    {
    }

    internal static class CompiledJson
    {
        private static readonly JsonSerializerOptions Options = new() {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = {
                new SemanticVersionConverter(),
            },
        };

        private static readonly CompiledJsonSourceGenerationContext Context = new(Options);

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

        public static VelopackFlowReleaseAsset[]? DeserializeVelopackFlowAssetArray(string json)
        {
            return JsonSerializer.Deserialize(json, Context.VelopackFlowReleaseAssetArray);
        }

        public static VelopackAssetFeed? DeserializeVelopackAssetFeed(string json)
        {
            return JsonSerializer.Deserialize(json, Context.VelopackAssetFeed);
        }

        public static string SerializeIpcMessage(IpcMessage message)
        {
            return JsonSerializer.Serialize(message, IpcJsonSourceGenerationContext.Default.IpcMessage);
        }

        public static IpcMessage? DeserializeIpcMessage(string json)
        {
            return JsonSerializer.Deserialize(json, IpcJsonSourceGenerationContext.Default.IpcMessage);
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
    internal static class CompiledJson
    {
        private static readonly JsonSerializerSettings Options = new() {
            Converters = { new StringEnumConverter(), new SemanticVersionConverter() },
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

        public static VelopackFlowReleaseAsset[]? DeserializeVelopackFlowAssetArray(string json)
        {
            return JsonConvert.DeserializeObject<VelopackFlowReleaseAsset[]>(json, Options);
        }

        public static VelopackAssetFeed? DeserializeVelopackAssetFeed(string json)
        {
            return JsonConvert.DeserializeObject<VelopackAssetFeed>(json, Options);
        }

        private static readonly JsonSerializerSettings IpcOptions = new() {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
        };

        public static string SerializeIpcMessage(IpcMessage message)
        {
            return JsonConvert.SerializeObject(message, IpcOptions);
        }

        public static IpcMessage? DeserializeIpcMessage(string json)
        {
            return JsonConvert.DeserializeObject<IpcMessage>(json, IpcOptions);
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
#endif
}