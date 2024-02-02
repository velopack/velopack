using System.Text.RegularExpressions;

namespace Velopack.Tests.OldSquirrel;

public static class VersionExtensions
{
    static readonly Regex _suffixRegex = new Regex(@"(-full|-delta)?\.nupkg$", RegexOptions.Compiled);
    static readonly Regex _versionRegex = new Regex(@"\d+(\.\d+){0,3}(-[A-Za-z][0-9A-Za-z-]*)?$", RegexOptions.Compiled);

    //public static SemanticVersion ToSemanticVersion(this IReleasePackage package)
    //{
    //    return package.InputPackageFile.ToSemanticVersion();
    //}

    public static SemanticVersion ToSemanticVersion(this string fileName)
    {
        var name = _suffixRegex.Replace(fileName, "");
        var version = _versionRegex.Match(name).Value;
        return new SemanticVersion(version);
    }
}