#nullable disable
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace Velopack
{
    /// <summary>
    /// Describes the requested release notes text format.
    /// </summary>
    [Obsolete("This release format has been replaced by VelopackRelease")]
    public enum ReleaseNotesFormat
    {
        /// <summary> The original markdown release notes. </summary>
        Markdown = 0,
        /// <summary> Release notes translated into HTML. </summary>
        Html = 1,
    }

    /// <summary>
    /// Represents the information that can be parsed from a release entry filename.
    /// </summary>
    [Obsolete("This release format has been replaced by VelopackRelease")]
    public sealed record ReleaseEntryName
    {
        /// <summary> The package Id. </summary>
        public string PackageId { get; private set; }

        /// <summary> The package version. </summary>
        public SemanticVersion Version { get; private set; }

        /// <summary> Whether this is a delta (patch) package, or a full update package. </summary>
        public bool IsDelta { get; private set; }

        private static readonly Regex _suffixRegex = new Regex(@"(-full|-delta)?\.nupkg$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _versionStartRegex = new Regex(@"[\.-](0|[1-9]\d*)\.(0|[1-9]\d*)($|[^\d])", RegexOptions.Compiled);

        /// <summary>
        /// Create a new ReleaseEntryName from the given package name, version, delta status, and runtime identifier.
        /// </summary>
        public ReleaseEntryName(string packageName, SemanticVersion version, bool isDelta)
        {
            PackageId = packageName;
            Version = version;
            IsDelta = isDelta;
        }

        /// <summary>
        /// Takes a filename such as 'My-Cool3-App-1.0.1-build.23-full.nupkg' and separates it into 
        /// it's name and version (eg. 'My-Cool3-App', and '1.0.1-build.23'). Returns null values if 
        /// the filename can not be parsed.
        /// </summary>
        public static ReleaseEntryName FromEntryFileName(string fileName)
        {
            if (!fileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                return new ReleaseEntryName(null, null, false);

            bool delta = Path.GetFileNameWithoutExtension(fileName).EndsWith("-delta", StringComparison.OrdinalIgnoreCase);

            var nameAndVer = _suffixRegex.Replace(Path.GetFileName(fileName), "");

            var match = _versionStartRegex.Match(nameAndVer);
            if (!match.Success)
                return new ReleaseEntryName(null, null, delta);

            var verIdx = match.Index;
            var name = nameAndVer.Substring(0, verIdx);
            var version = nameAndVer.Substring(verIdx + 1);



            var semVer = NuGetVersion.Parse(version);
            return new ReleaseEntryName(name, semVer, delta);
        }

        /// <summary>
        /// Generate the file name which would represent this ReleaseEntryName.
        /// </summary>
        public string ToFileName() =>
            $"{PackageId}-{Version}{(IsDelta ? "-delta" : "-full")}.nupkg";
    }

    /// <summary>
    /// Represents a Velopack release, as described in a RELEASES file - usually also with an 
    /// accompanying package containing the files needed to apply the release.
    /// </summary>
    [DataContract]
    [Obsolete("This release format has been replaced by VelopackRelease")]
    public class ReleaseEntry
    {
        /// <summary> The release identity - including id, version and so forth. </summary>*
        [IgnoreDataMember] public ReleaseEntryName Identity { get; protected set; }

        /// <summary> The name or Id of the package containing this release. </summary>
        [DataMember] public string PackageId => Identity.PackageId;

        /// <summary> The version of this release. </summary>
        [DataMember] public SemanticVersion Version => Identity.Version;

        /// <summary> Whether this package represents a full update, or a delta update. </summary>
        [DataMember] public bool IsDelta => Identity.IsDelta;

        /// <summary> The SHA1 checksum of the update package containing this release. </summary>
        [DataMember] public string SHA1 { get; protected set; }

        /// <summary> If the release corresponds to a remote http location, this will be the base url. </summary>
        [DataMember] public string BaseUrl { get; protected set; }

        /// <summary> The http url query (if applicable). </summary>
        [DataMember] public string Query { get; protected set; }

        /// <summary> The size in bytes of the update package containing this release. </summary>
        [DataMember] public long Filesize { get; protected set; }

        /// <summary> 
        /// The percentage of users this package has been released to. This release
        /// may or may not be applied if the current user is not in the staging group.
        /// </summary>
        [DataMember] public float? StagingPercentage { get; protected set; }

        /// <summary> The filename of the update package containing this release. </summary>
        [DataMember] public string OriginalFilename { get; protected set; }

        /// <summary> The unparsed text used to construct this release. </summary>
        [IgnoreDataMember]
        public string EntryAsString {
            get {
                if (StagingPercentage != null) {
                    return String.Format("{0} {1}{2} {3} # {4}", SHA1, BaseUrl, OriginalFilename, Filesize, stagingPercentageAsString(StagingPercentage.Value));
                } else {
                    return String.Format("{0} {1}{2} {3}", SHA1, BaseUrl, OriginalFilename, Filesize);
                }
            }
        }

        /// <summary>
        /// Create a new instance of <see cref="ReleaseEntry"/>.
        /// </summary>
        protected internal ReleaseEntry(string sha1, string filename, long filesize, string baseUrl = null, string query = null, float? stagingPercentage = null)
        {
            Contract.Requires(sha1 != null && sha1.Length == 40);
            Contract.Requires(filename != null);
            Contract.Requires(filename.Contains(Path.DirectorySeparatorChar) == false);
            Contract.Requires(filesize > 0);

            SHA1 = sha1;
            BaseUrl = baseUrl;
            Query = query;
            Filesize = filesize;
            StagingPercentage = stagingPercentage;
            OriginalFilename = filename;
            Identity = ReleaseEntryName.FromEntryFileName(filename);
        }

        ///// <summary>
        ///// Given a local directory containing a package corresponding to this release, returns the 
        ///// corresponding release notes from within the package.
        ///// </summary>
        //public string GetReleaseNotes(string packageDirectory, ReleaseNotesFormat format)
        //{
        //    var zp = new ZipPackage(Path.Combine(packageDirectory, Filename));
        //    return format switch {
        //        ReleaseNotesFormat.Markdown => zp.ReleaseNotes,
        //        ReleaseNotesFormat.Html => zp.ReleaseNotesHtml,
        //        _ => null,
        //    };
        //}

        ///// <inheritdoc />  
        //public Uri GetIconUrl(string packageDirectory)
        //{
        //    var zp = new ZipPackage(Path.Combine(packageDirectory, Filename));
        //    return zp.IconUrl;
        //}

        static readonly Regex entryRegex = new Regex(@"^([0-9a-fA-F]{40})\s+(\S+)\s+(\d+)[\r]*$");
        static readonly Regex commentRegex = new Regex(@"\s*#.*$");
        static readonly Regex stagingRegex = new Regex(@"#\s+(\d{1,3})%$");

        public static ReleaseEntry FromVelopackAsset(VelopackAsset asset)
        {
            return new ReleaseEntry(asset.SHA1, asset.FileName, asset.Size);
        }

        /// <summary>
        /// Parses an string entry from a RELEASES file and returns a <see cref="ReleaseEntry"/>.
        /// </summary>
        public static ReleaseEntry ParseReleaseEntry(string entry)
        {
            Contract.Requires(entry != null);

            float? stagingPercentage = null;
            var m = stagingRegex.Match(entry);
            if (m != null && m.Success) {
                stagingPercentage = Single.Parse(m.Groups[1].Value) / 100.0f;
            }

            entry = commentRegex.Replace(entry, "");
            if (String.IsNullOrWhiteSpace(entry)) {
                return null;
            }

            m = entryRegex.Match(entry);
            if (!m.Success) {
                throw new Exception("Invalid release entry: " + entry);
            }

            if (m.Groups.Count != 4) {
                throw new Exception("Invalid release entry: " + entry);
            }

            string filename = m.Groups[2].Value;

            // Split the base URL and the filename if an URI is provided,
            // throws if a path is provided
            string baseUrl = null;
            string query = null;

            if (Utility.IsHttpUrl(filename)) {
                var uri = new Uri(filename);
                var path = uri.LocalPath;
                var authority = uri.GetLeftPart(UriPartial.Authority);

                if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(authority)) {
                    throw new Exception("Invalid URL");
                }

                var indexOfLastPathSeparator = path.LastIndexOf("/") + 1;
                baseUrl = authority + path.Substring(0, indexOfLastPathSeparator);
                filename = path.Substring(indexOfLastPathSeparator);

                if (!String.IsNullOrEmpty(uri.Query)) {
                    query = uri.Query;
                }
            }

            if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) > -1) {
                throw new Exception("Filename can either be an absolute HTTP[s] URL, *or* a file name");
            }

            if (filename.IndexOfAny(new[] { '\"', '/', '\\', '<', '>', '|', '\0' }) > -1) {
                throw new Exception("Filename can either be an absolute HTTP[s] URL, *or* a file name");
            }

            long size = Int64.Parse(m.Groups[3].Value);
            return new ReleaseEntry(m.Groups[1].Value, filename, size, baseUrl, query, stagingPercentage);
        }

        /// <summary>
        /// Checks if the current user is eligible for the current staging percentage.
        /// </summary>
        public bool IsStagingMatch(Guid? userId)
        {
            // A "Staging match" is when a user falls into the affirmative
            // bucket - i.e. if the staging is at 10%, this user is the one out
            // of ten case.
            if (!StagingPercentage.HasValue) return true;
            if (!userId.HasValue) return false;

            uint val = BitConverter.ToUInt32(userId.Value.ToByteArray(), 12);

            double percentage = ((double) val / (double) UInt32.MaxValue);
            return percentage < StagingPercentage.Value;
        }

        /// <summary>
        /// Parse the contents of a RELEASES file into a list of <see cref="ReleaseEntry"/>'s.
        /// </summary>
        public static IEnumerable<ReleaseEntry> ParseReleaseFile(string fileContents)
        {
            if (String.IsNullOrEmpty(fileContents)) {
                return new ReleaseEntry[0];
            }

            fileContents = Utility.RemoveByteOrderMarkerIfPresent(fileContents);

            var ret = fileContents.Split('\n')
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(ParseReleaseEntry)
                .Where(x => x != null)
                .ToArray();

            return ret.Any(x => x == null) ? new ReleaseEntry[0] : ret;
        }

        /// <summary>
        /// Parse the contents of a RELEASES file into a list of <see cref="ReleaseEntry"/>'s,
        /// with any staging-ineligible releases removed.
        /// </summary>
        public static IEnumerable<ReleaseEntry> ParseReleaseFileAndApplyStaging(string fileContents, Guid? userToken)
        {
            if (String.IsNullOrEmpty(fileContents)) {
                return new ReleaseEntry[0];
            }

            fileContents = Utility.RemoveByteOrderMarkerIfPresent(fileContents);

            var ret = fileContents.Split('\n')
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(ParseReleaseEntry)
                .Where(x => x != null && x.IsStagingMatch(userToken))
                .ToArray();

            return ret.Any(x => x == null) ? null : ret;
        }

        /// <summary>
        /// Write a list of <see cref="ReleaseEntry"/>'s to a stream
        /// </summary>
        public static void WriteReleaseFile(IEnumerable<ReleaseEntry> releaseEntries, Stream stream)
        {
            Contract.Requires(releaseEntries != null && releaseEntries.Any());
            Contract.Requires(stream != null);

            using (var sw = new StreamWriter(stream, Encoding.UTF8)) {
                sw.Write(String.Join("\n", releaseEntries
                    .OrderBy(x => x.Version)
                    .ThenByDescending(x => x.IsDelta)
                    .Select(x => x.EntryAsString)));
            }
        }

        /// <summary>
        /// Write a list of <see cref="ReleaseEntry"/>'s to a local file
        /// </summary>
        public static void WriteReleaseFile(IEnumerable<ReleaseEntry> releaseEntries, string path)
        {
            Contract.Requires(releaseEntries != null && releaseEntries.Any());
            Contract.Requires(!String.IsNullOrEmpty(path));

            using (var f = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None)) {
                WriteReleaseFile(releaseEntries, f);
            }
        }

        /// <summary>
        /// Generates a <see cref="ReleaseEntry"/> from a local update package file (such as a nupkg).
        /// </summary>
        public static ReleaseEntry GenerateFromFile(Stream file, string filename, string baseUrl = null)
        {
            Contract.Requires(file != null && file.CanRead);
            Contract.Requires(!String.IsNullOrEmpty(filename));

            var hash = Utility.CalculateStreamSHA1(file);
            return new ReleaseEntry(hash, filename, file.Length, baseUrl);
        }

        /// <summary>
        /// Generates a <see cref="ReleaseEntry"/> from a local update package file (such as a nupkg).
        /// </summary>
        public static ReleaseEntry GenerateFromFile(string path, string baseUrl = null)
        {
            using (var inf = File.OpenRead(path)) {
                return GenerateFromFile(inf, Path.GetFileName(path), baseUrl);
            }
        }

        /// <summary>
        /// Generates a list of <see cref="ReleaseEntry"/>'s from a local directory containing
        /// package files. Also writes/updates a RELEASES file in the specified directory
        /// to match the packages the are currently present.
        /// </summary>
        /// <returns>The list of packages in the directory</returns>
        public static List<ReleaseEntry> BuildReleasesFile(string releasePackagesDir, bool writeToDisk = true)
        {
            var packagesDir = new DirectoryInfo(releasePackagesDir);

            // Generate release entries for all of the local packages
            var entriesQueue = new ConcurrentQueue<ReleaseEntry>();
            Parallel.ForEach(packagesDir.GetFiles("*.nupkg"), x => {
                using (var file = x.OpenRead()) {
                    entriesQueue.Enqueue(GenerateFromFile(file, x.Name));
                }
            });

            // Write the new RELEASES file to a temp file then move it into
            // place
            var entries = entriesQueue.ToList();

            if (writeToDisk) {
                using var _ = Utility.GetTempFileName(out var tempFile);
                using (var of = File.OpenWrite(tempFile)) {
                    if (entries.Count > 0) WriteReleaseFile(entries, of);
                }
                var target = Path.Combine(packagesDir.FullName, "RELEASES");
                if (File.Exists(target)) {
                    File.Delete(target);
                }

                File.Move(tempFile, target);
            }

            return entries;
        }

        static string stagingPercentageAsString(float percentage)
        {
            return String.Format("{0:F0}%", percentage * 100.0);
        }

        /// <inheritdoc />
        public override string ToString() => Identity.ToFileName();

        /// <inheritdoc />
        public override int GetHashCode() => Identity.GetHashCode();
    }
}
