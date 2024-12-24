using NuGet.Versioning;

namespace Velopack.Core;

public static class DefaultName
{
    public static string GetSuggestedReleaseName(string id, string version, string channel, bool delta, RuntimeOs os)
    {
        var suffix = GetUniqueAssetSuffix(channel);
        version = SemanticVersion.Parse(version).ToNormalizedString();
        if (os == RuntimeOs.Windows && channel == GetDefaultChannel(RuntimeOs.Windows)) {
            return $"{id}-{version}{(delta ? "-delta" : "-full")}.nupkg";
        }

        return $"{id}-{version}{suffix}{(delta ? "-delta" : "-full")}.nupkg";
    }

    public static string GetSuggestedPortableName(string id, string channel, RuntimeOs os)
    {
        var suffix = GetUniqueAssetSuffix(channel);
        if (os == RuntimeOs.Linux) {
            if (channel == GetDefaultChannel(RuntimeOs.Linux)) {
                return $"{id}.AppImage";
            } else {
                return $"{id}{suffix}.AppImage";
            }
        } else {
            return $"{id}{suffix}-Portable.zip";
        }
    }

    public static string GetSuggestedSetupName(string id, string channel, RuntimeOs os)
    {
        var suffix = GetUniqueAssetSuffix(channel);
        if (os == RuntimeOs.Windows)
            return $"{id}{suffix}-Setup.exe";
        else if (os == RuntimeOs.OSX)
            return $"{id}{suffix}-Setup.pkg";
        else
            throw new PlatformNotSupportedException("Platform not supported.");
    }

    private static string GetUniqueAssetSuffix(string channel)
    {
        return "-" + channel;
    }

    public static string GetDefaultChannel(RuntimeOs os)
    {
        if (os == RuntimeOs.Windows) return "win";
        if (os == RuntimeOs.OSX) return "osx";
        if (os == RuntimeOs.Linux) return "linux";
        throw new NotSupportedException("Unsupported OS: " + os);
    }
}