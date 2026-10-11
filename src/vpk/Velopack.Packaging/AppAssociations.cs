#nullable enable
using System.Text.RegularExpressions;
using Velopack.Core;

namespace Velopack.Packaging;

/// <summary>
/// Normalizes and validates the <c>--fileAssociations</c> and <c>--urlProtocols</c> pack options.
/// Shared by the Windows packager (nuspec metadata, registered by Update.exe) and the macOS
/// packager (Info.plist CFBundleDocumentTypes / CFBundleURLTypes) so both emit identical lists.
/// </summary>
public static class AppAssociations
{
    private static readonly char[] ListSeparators = [',', ';'];

    private static readonly Regex ExtensionRegex = new(@"^\.[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.Compiled);

    // RFC 3986 section 3.1 scheme syntax, after lowercasing. Bounded like extensions: the Windows registry key
    // name limit is 255 characters, and Update.exe cannot register anything vpk accepted beyond that.
    private static readonly Regex SchemeRegex = new(@"^[a-z][a-z0-9+.-]{0,63}$", RegexOptions.Compiled);

    // schemes which are owned by the OS / browsers and must never be redirected to an app.
    private static readonly HashSet<string> ReservedSchemes = new(StringComparer.Ordinal) {
        "http", "https", "file", "ftp", "mailto", "javascript", "data", "about", "blob",
        "ws", "wss", "tel", "sms", "news", "ldap", "res", "shell",
    };

    /// <summary>
    /// Parses a comma or semicolon separated list of file extensions (eg. ".myproj,mytpl") into
    /// normalized, distinct, lowercase extensions with a leading dot (eg. [".myproj", ".mytpl"]).
    /// </summary>
    /// <exception cref="UserInfoException">An entry is not a valid file extension.</exception>
    public static string[] ParseFileAssociations(string? value)
    {
        return Split(value)
            .Select(NormalizeExtension)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Parses a comma or semicolon separated list of URL schemes (eg. "myapp,myapp-beta://") into
    /// normalized, distinct, lowercase scheme names without any trailing separator.
    /// </summary>
    /// <exception cref="UserInfoException">An entry is not a valid, non-reserved URL scheme.</exception>
    public static string[] ParseUrlProtocols(string? value)
    {
        return Split(value)
            .Select(NormalizeScheme)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Joins a normalized list back into the comma-delimited form stored in package metadata.</summary>
    public static string Join(IEnumerable<string> values) => string.Join(",", values);

    private static IEnumerable<string> Split(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }

        return value!.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim().ToLowerInvariant())
            .Where(v => v.Length > 0);
    }

    private static string NormalizeExtension(string raw)
    {
        var ext = raw.StartsWith('.') ? raw : "." + raw;
        if (!ExtensionRegex.IsMatch(ext)) {
            throw new UserInfoException(
                $"Invalid file association '{raw}'. Extensions must be 1-64 characters of a-z, 0-9, '-' or '_' " +
                $"(starting with a letter or digit), for example '.myproj'.");
        }

        return ext;
    }

    private static string NormalizeScheme(string raw)
    {
        var scheme = raw;
        if (scheme.EndsWith("://", StringComparison.Ordinal)) {
            scheme = scheme.Substring(0, scheme.Length - 3);
        } else if (scheme.EndsWith(':')) {
            scheme = scheme.Substring(0, scheme.Length - 1);
        }

        if (!SchemeRegex.IsMatch(scheme)) {
            throw new UserInfoException(
                $"Invalid URL protocol '{raw}'. Schemes must be 1-64 characters, start with a letter and contain only " +
                $"a-z, 0-9, '+', '-' or '.', for example 'myapp'.");
        }

        if (ReservedSchemes.Contains(scheme) || scheme.StartsWith("ms-", StringComparison.Ordinal)) {
            throw new UserInfoException($"Invalid URL protocol '{raw}'. The '{scheme}' scheme is reserved and cannot be registered.");
        }

        return scheme;
    }
}
