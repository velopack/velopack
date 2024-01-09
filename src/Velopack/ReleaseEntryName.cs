using System;
using System.IO;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace Velopack
{
    /// <summary>
    /// Represents the information that can be parsed from a release entry filename.
    /// </summary>
    public sealed record ReleaseEntryName
    {
        /// <summary> The package Id. </summary>
        public string PackageId { get; private set; }

        /// <summary> The package version. </summary>
        public SemanticVersion Version { get; private set; }

        /// <summary> Whether this is a delta (patch) package, or a full update package. </summary>
        public bool IsDelta { get; private set; }

        /// <summary> The target machine runtime identifier (eg. win-x64). </summary>
        public RID Rid { get; private set; }

        private static readonly Regex _suffixRegex = new Regex(@"(-full|-delta)?\.nupkg$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _versionStartRegex = new Regex(@"[\.-](0|[1-9]\d*)\.(0|[1-9]\d*)($|[^\d])", RegexOptions.Compiled);
        private static readonly Regex _ridRegex = new Regex(@"-(?:(?<os>osx|win|linux)(?<ver>[\d\.]+)?)?-?(?<arch>x64|x86|arm64)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Create a new ReleaseEntryName from the given package name, version, delta status, and runtime identifier.
        /// </summary>
        public ReleaseEntryName(string packageName, SemanticVersion version, bool isDelta, RID rid)
        {
            PackageId = packageName;
            Version = version;
            IsDelta = isDelta;
            Rid = rid;
        }

        /// <summary>
        /// Takes a filename such as 'My-Cool3-App-1.0.1-build.23-full.nupkg' and separates it into 
        /// it's name and version (eg. 'My-Cool3-App', and '1.0.1-build.23'). Returns null values if 
        /// the filename can not be parsed.
        /// </summary>
        public static ReleaseEntryName FromEntryFileName(string fileName)
        {
            if (!fileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                return new ReleaseEntryName(null, null, false, null);

            bool delta = Path.GetFileNameWithoutExtension(fileName).EndsWith("-delta", StringComparison.OrdinalIgnoreCase);

            var nameAndVer = _suffixRegex.Replace(Path.GetFileName(fileName), "");

            var match = _versionStartRegex.Match(nameAndVer);
            if (!match.Success)
                return new ReleaseEntryName(null, null, delta, null);

            var verIdx = match.Index;
            var name = nameAndVer.Substring(0, verIdx);
            var version = nameAndVer.Substring(verIdx + 1);

            RID rid = null;
            var ridMatch = _ridRegex.Match(version);

            if (ridMatch.Success) {
                var ridStr = ridMatch.Value.TrimStart('-', '[').TrimEnd(']');
                rid = RID.Parse(ridStr);
                version = version.Substring(0, ridMatch.Index);
            }

            var semVer = NuGetVersion.Parse(version);
            return new ReleaseEntryName(name, semVer, delta, rid);
        }

        /// <summary>
        /// Generate the file name which would represent this ReleaseEntryName.
        /// </summary>
        public string ToFileName() =>
            $"{PackageId}-{Version}{(Rid != null ? $"-{Rid.ToDisplay(RidDisplayType.NoVersion)}" : "")}{(IsDelta ? "-delta" : "-full")}.nupkg";
    }
}
