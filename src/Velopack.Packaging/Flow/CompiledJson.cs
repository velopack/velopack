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

namespace Velopack.Packaging.Flow
{
#if NET6_0_OR_GREATER

    [JsonSerializable(typeof(ReleaseGroup))]
    [JsonSerializable(typeof(CreateReleaseGroupRequest))]
#if NET8_0_OR_GREATER
    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
#endif
    internal partial class CompiledJsonSourceGenerationContext : JsonSerializerContext
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
#if !NET8_0_OR_GREATER
                new JsonStringEnumConverter(),
#endif
            },
        };

        private static readonly CompiledJsonSourceGenerationContext Context = new(Options);
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
            Converters = { new StringEnumConverter() },
            ContractResolver = new JsonNameContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };
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