using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Velopack
{
    /// <summary>
    /// A semantic version implementation that supports SemVer2 with an optional 4th revision component
    /// for backwards compatibility with NuGet-style version numbers.
    /// </summary>
    public class SemanticVersion : IEquatable<SemanticVersion>, IComparable<SemanticVersion>, IComparable
    {
        /// <summary> The major version component. </summary>
        public int Major { get; }

        /// <summary> The minor version component. </summary>
        public int Minor { get; }

        /// <summary> The patch version component. </summary>
        public int Patch { get; }

        /// <summary> The optional fourth version component, or 0 if not specified. </summary>
        public int Revision { get; }

        /// <summary> A basic four-part version ignoring release labels and metadata. </summary>
        public Version Version => new(Major, Minor, Patch, Revision);

        /// <summary> The full pre-release label string (e.g. "beta.1"), or empty string if not a pre-release. </summary>
        public string Release { get; }

        /// <summary> The dot-separated pre-release identifiers. </summary>
        public IEnumerable<string> ReleaseLabels =>
            string.IsNullOrEmpty(Release) ? Enumerable.Empty<string>() : Release.Split('.');

        /// <summary> True if this version has a pre-release label. </summary>
        public bool IsPrerelease => !string.IsNullOrEmpty(Release);

        /// <summary> The build metadata string, or empty string if none. </summary>
        public string Metadata { get; }

        /// <summary> True if this version has build metadata. </summary>
        public bool HasMetadata => !string.IsNullOrEmpty(Metadata);

        /// <summary>
        /// Creates a new SemanticVersion.
        /// </summary>
        /// <param name="major">The major version number.</param>
        /// <param name="minor">The minor version number.</param>
        /// <param name="patch">The patch version number.</param>
        /// <param name="prerelease">The pre-release label (e.g. "beta.1"), or empty/null for a stable release.</param>
        /// <param name="metadata">The build metadata string, or empty/null for no metadata.</param>
        public SemanticVersion(int major, int minor, int patch, string prerelease = "", string metadata = "")
            : this(major, minor, patch, 0, prerelease, metadata)
        {
        }
        
        /// <summary>
        /// Creates a new SemanticVersion from a traditional 4-part Version.
        /// </summary>
        /// <param name="version">The version containing major, minor, patch, and revision.</param>
        /// <param name="prerelease">The pre-release label (e.g. "beta.1"), or empty/null for a stable release.</param>
        /// <param name="metadata">The build metadata string, or empty/null for no metadata.</param>
        public SemanticVersion(Version version, string prerelease = "", string metadata = "")
            : this(version.Major, version.Minor, version.Build, version.Revision, prerelease, metadata)
        {
        }

        /// <summary>
        /// Creates a new SemanticVersion with a revision component.
        /// </summary>
        /// <param name="major">The major version number.</param>
        /// <param name="minor">The minor version number.</param>
        /// <param name="patch">The patch version number.</param>
        /// <param name="revision">The fourth version component.</param>
        /// <param name="prerelease">The pre-release label, or empty/null for a stable release.</param>
        /// <param name="metadata">The build metadata string, or empty/null for no metadata.</param>
        public SemanticVersion(int major, int minor, int patch, int revision, string prerelease = "", string metadata = "")
        {
            if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
            if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
            if (patch < 0) throw new ArgumentOutOfRangeException(nameof(patch));
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            Major = major;
            Minor = minor;
            Patch = patch;
            Revision = revision;
            Release = prerelease ?? "";
            Metadata = metadata ?? "";
        }
        
        /// <summary>
        /// Parse a version string into a SemanticVersion. Throws <see cref="ArgumentException"/> on invalid input.
        /// </summary>
        public static SemanticVersion Parse(string value)
        {
            if (TryParse(value, out var result))
                return result;
            throw new ArgumentException($"'{value}' is not a valid semantic version.", nameof(value));
        }

        /// <summary>
        /// Try to parse a version string. Returns false if the string is not a valid semantic version.
        /// </summary>
        public static bool TryParse(string? value, [NotNullWhen(true)] out SemanticVersion? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value!.Trim();

            // Split off metadata first (+...)
            string metadata = "";
            var metaIdx = value.IndexOf('+');
            if (metaIdx >= 0) {
                metadata = value.Substring(metaIdx + 1);
                value = value.Substring(0, metaIdx);
            }

            // Split off prerelease (-...)
            string prerelease = "";
            var preIdx = value.IndexOf('-');
            if (preIdx >= 0) {
                prerelease = value.Substring(preIdx + 1);
                value = value.Substring(0, preIdx);
                if (string.IsNullOrEmpty(prerelease))
                    return false;
            }

            // Parse version parts (accept 2-4 parts)
            var parts = value.Split('.');
            if (parts.Length < 2 || parts.Length > 4)
                return false;

            if (!int.TryParse(parts[0], out var major) || major < 0)
                return false;
            if (!int.TryParse(parts[1], out var minor) || minor < 0)
                return false;

            int patch = 0;
            if (parts.Length >= 3) {
                if (!int.TryParse(parts[2], out patch) || patch < 0)
                    return false;
            }

            int revision = 0;
            if (parts.Length == 4) {
                if (!int.TryParse(parts[3], out revision) || revision < 0)
                    return false;
            }

            version = new SemanticVersion(major, minor, patch, revision, prerelease, metadata);
            return true;
        }

        /// <summary>
        /// Compares two versions by Major.Minor.Patch.Revision only, ignoring pre-release and metadata.
        /// </summary>
        public static int CompareByVersion(SemanticVersion? a, SemanticVersion? b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return -1;
            if (b is null) return 1;

            var r = a.Major.CompareTo(b.Major);
            if (r != 0) return r;
            r = a.Minor.CompareTo(b.Minor);
            if (r != 0) return r;
            r = a.Patch.CompareTo(b.Patch);
            if (r != 0) return r;
            return a.Revision.CompareTo(b.Revision);
        }

        private string VersionString()
        {
            if (Revision > 0)
                return $"{Major}.{Minor}.{Patch}.{Revision}";
            return $"{Major}.{Minor}.{Patch}";
        }

        /// <summary>
        /// Returns the full version string including metadata (e.g. "1.2.3-beta+build").
        /// </summary>
        public string ToFullString()
        {
            var s = VersionString();
            if (IsPrerelease) s += "-" + Release;
            if (HasMetadata) s += "+" + Metadata;
            return s;
        }

        /// <summary>
        /// Returns the normalized version string without metadata (e.g. "1.2.3-beta").
        /// </summary>
        public string ToNormalizedString()
        {
            var s = VersionString();
            if (IsPrerelease) s += "-" + Release;
            return s;
        }

        /// <inheritdoc/>
        public override string ToString() => ToNormalizedString();

        /// <inheritdoc/>
        public int CompareTo(SemanticVersion? other)
        {
            if (other is null) return 1;

            var r = Major.CompareTo(other.Major);
            if (r != 0) return r;
            r = Minor.CompareTo(other.Minor);
            if (r != 0) return r;
            r = Patch.CompareTo(other.Patch);
            if (r != 0) return r;
            r = Revision.CompareTo(other.Revision);
            if (r != 0) return r;

            // SemVer2: no prerelease > has prerelease
            if (!IsPrerelease && other.IsPrerelease) return 1;
            if (IsPrerelease && !other.IsPrerelease) return -1;
            if (!IsPrerelease && !other.IsPrerelease) return 0;

            // Compare prerelease identifiers dot by dot
            var thisLabels = ReleaseLabels.ToArray();
            var otherLabels = other.ReleaseLabels.ToArray();
            var len = Math.Min(thisLabels.Length, otherLabels.Length);

            for (int i = 0; i < len; i++) {
                var thisIsNum = int.TryParse(thisLabels[i], out var thisNum);
                var otherIsNum = int.TryParse(otherLabels[i], out var otherNum);

                if (thisIsNum && otherIsNum) {
                    r = thisNum.CompareTo(otherNum);
                    if (r != 0) return r;
                } else if (thisIsNum) {
                    return -1; // numeric < alpha
                } else if (otherIsNum) {
                    return 1;
                } else {
                    r = StringComparer.OrdinalIgnoreCase.Compare(thisLabels[i], otherLabels[i]);
                    if (r != 0) return r;
                }
            }

            return thisLabels.Length.CompareTo(otherLabels.Length);
        }

        /// <inheritdoc/>
        public int CompareTo(object? obj)
        {
            if (obj is null) return 1;
            if (obj is SemanticVersion other) return CompareTo(other);
            throw new ArgumentException("Object is not a SemanticVersion.");
        }

        /// <inheritdoc/>
        public bool Equals(SemanticVersion? other)
        {
            if (other is null) return false;
            return CompareTo(other) == 0;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as SemanticVersion);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked {
                var hash = 17;
                hash = hash * 31 + Major;
                hash = hash * 31 + Minor;
                hash = hash * 31 + Patch;
                hash = hash * 31 + Revision;
                if (IsPrerelease) {
                    hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(Release);
                }

                return hash;
            }
        }

        /// <summary> Equality operator. </summary>
        public static bool operator ==(SemanticVersion? left, SemanticVersion? right) =>
            left is null ? right is null : left.Equals(right);

        /// <summary> Inequality operator. </summary>
        public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

        /// <summary> Less-than operator. </summary>
        public static bool operator <(SemanticVersion? left, SemanticVersion? right) =>
            left is null ? right is not null : left.CompareTo(right) < 0;

        /// <summary> Greater-than operator. </summary>
        public static bool operator >(SemanticVersion? left, SemanticVersion? right) =>
            left is not null && left.CompareTo(right) > 0;

        /// <summary> Less-than-or-equal operator. </summary>
        public static bool operator <=(SemanticVersion? left, SemanticVersion? right) =>
            left is null || left.CompareTo(right) <= 0;

        /// <summary> Greater-than-or-equal operator. </summary>
        public static bool operator >=(SemanticVersion? left, SemanticVersion? right) =>
            left is null ? right is null : left.CompareTo(right) >= 0;
    }
}