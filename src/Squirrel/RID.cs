#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
// https://github.com/dotnet/runtime/tree/main/src/libraries/Microsoft.NETCore.Platforms

using System;
using System.Diagnostics;
using System.Text;

namespace Squirrel
{
    /// <summary>
    /// A Version class that also supports a single integer (major only)
    /// </summary>
    public sealed class RuntimeVersion : IComparable, IComparable<RuntimeVersion>, IEquatable<RuntimeVersion>
    {
        public int Major => version.Major;

        private string versionString;
        private Version version;
        private bool hasMinor;

        public RuntimeVersion(string versionString)
        {
            // intentionally don't support the type of version that omits the separators as it is abiguous.
            // for example Windows 8.1 was encoded as win81, where as Windows 10.0 was encoded as win10
            this.versionString = versionString;
            string toParse = versionString;
#if NETCOREAPP
            if (!toParse.Contains('.'))
#else
            if (toParse.IndexOf('.') == -1)
#endif
            {
                toParse += ".0";
                hasMinor = false;
            } else {
                hasMinor = true;
            }

            version = Version.Parse(toParse);
        }

        //public string To3Part()
        //{
        //    return $"{Math.Max(0, version.Major)}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}";
        //}

        public int CompareTo(object obj)
        {
            if (obj == null) {
                return 1;
            }

            if (obj is RuntimeVersion version) {
                return CompareTo(version);
            }

            throw new ArgumentException($"Cannot compare {nameof(RuntimeVersion)} to object of type {obj.GetType()}.", nameof(obj));
        }

        public int CompareTo(RuntimeVersion other)
        {
            if (other == null) {
                return 1;
            }

            int versionResult = version.CompareTo(other?.version);

            if (versionResult == 0) {
                if (!hasMinor && other.hasMinor) {
                    return -1;
                }

                if (hasMinor && !other.hasMinor) {
                    return 1;
                }

                return string.CompareOrdinal(versionString, other.versionString);
            }

            return versionResult;
        }

        public bool Equals(RuntimeVersion other)
        {
            return object.ReferenceEquals(other, this) ||
                   (other != null &&
                    versionString.Equals(other.versionString, StringComparison.Ordinal));
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as RuntimeVersion);
        }

        public override int GetHashCode()
        {
            return versionString.GetHashCode();
        }

        public override string ToString()
        {
            return versionString;
        }

        public static bool operator ==(RuntimeVersion v1, RuntimeVersion v2)
        {
            if (v2 is null) {
                return (v1 is null) ? true : false;
            }

            return ReferenceEquals(v2, v1) ? true : v2.Equals(v1);
        }

        public static bool operator !=(RuntimeVersion v1, RuntimeVersion v2) => !(v1 == v2);

        public static bool operator <(RuntimeVersion v1, RuntimeVersion v2)
        {
            if (v1 is null) {
                return !(v2 is null);
            }

            return v1.CompareTo(v2) < 0;
        }

        public static bool operator <=(RuntimeVersion v1, RuntimeVersion v2)
        {
            if (v1 is null) {
                return true;
            }

            return v1.CompareTo(v2) <= 0;
        }

        public static bool operator >(RuntimeVersion v1, RuntimeVersion v2) => v2 < v1;

        public static bool operator >=(RuntimeVersion v1, RuntimeVersion v2) => v2 <= v1;
    }

    public enum RidDisplayType
    {
        NoVersion,
        ShortVersion,
        FullVersion,
    }

    public class RID
    {
        internal const char VersionDelimiter = '.';
        internal const char ArchitectureDelimiter = '-';
        internal const char QualifierDelimiter = '-';

        public RuntimeOs BaseRID { get; set; }
        //public bool OmitVersionDelimiter { get; set; }
        public RuntimeVersion Version { get; set; }
        public RuntimeCpu Architecture { get; set; }
        public string Qualifier { get; set; }

        public override string ToString() => ToDisplay(RidDisplayType.FullVersion);

        public string ToDisplay(RidDisplayType type)
        {
            if (!IsValid) return "";
            StringBuilder builder = new StringBuilder(BaseRID.GetOsShortName());

            if (HasVersion) {
                //if (!OmitVersionDelimiter) {
                //    builder.Append(VersionDelimiter);
                //}

                if (type == RidDisplayType.FullVersion) {
                    builder.Append(Version);
                } else if (type == RidDisplayType.ShortVersion) {
                    builder.Append(Version.Major);
                }
            }

            if (HasArchitecture) {
                builder.Append(ArchitectureDelimiter);
                builder.Append(Architecture);
            }

            if (HasQualifier) {
                builder.Append(QualifierDelimiter);
                builder.Append(Qualifier);
            }

            return builder.ToString();
        }

        private enum RIDPart : int
        {
            Base = 0,
            Version,
            Architecture,
            Qualifier,
            Max = Qualifier
        }

        public static RID Parse(string runtimeIdentifier)
        {
            string[] parts = new string[(int) RIDPart.Max + 1];
            //bool omitVersionDelimiter = true;
            RIDPart parseState = RIDPart.Base;

            int partStart = 0, partLength;

            // qualifier is indistinguishable from arch so we cannot distinguish it for parsing purposes
            Debug.Assert(ArchitectureDelimiter == QualifierDelimiter);

            for (int i = 0; i < runtimeIdentifier.Length; i++) {
                char current = runtimeIdentifier[i];
                partLength = i - partStart;

                switch (parseState) {
                case RIDPart.Base:
                    // treat any number as the start of the version
                    if (current == VersionDelimiter || (current >= '0' && current <= '9')) {
                        SetPart();
                        partStart = i;
                        if (current == VersionDelimiter) {
                            //omitVersionDelimiter = false;
                            partStart = i + 1;
                        }

                        parseState = RIDPart.Version;
                    }
                    // version might be omitted
                    else if (current == ArchitectureDelimiter) {
                        // ensure there's no version later in the string
                        if (runtimeIdentifier.IndexOf(VersionDelimiter, i) != -1) {
                            break;
                        }

                        SetPart();
                        partStart = i + 1; // skip delimiter
                        parseState = RIDPart.Architecture;
                    }

                    break;
                case RIDPart.Version:
                    if (current == ArchitectureDelimiter) {
                        SetPart();
                        partStart = i + 1; // skip delimiter
                        parseState = RIDPart.Architecture;
                    }

                    break;
                case RIDPart.Architecture:
                    if (current == QualifierDelimiter) {
                        SetPart();
                        partStart = i + 1; // skip delimiter
                        parseState = RIDPart.Qualifier;
                    }

                    break;
                default:
                    break;
                }
            }

            partLength = runtimeIdentifier.Length - partStart;
            if (partLength > 0) {
                SetPart();
            }

            string GetPart(RIDPart part)
            {
                return parts[(int) part];
            }

            void SetPart()
            {
                if (partLength == 0) {
                    throw new ArgumentException($"Unexpected delimiter at position {partStart} in \"{runtimeIdentifier}\"");
                }

                parts[(int) parseState] = runtimeIdentifier.Substring(partStart, partLength);
            }

            string version = GetPart(RIDPart.Version);

            //if (version == null) {
            //    omitVersionDelimiter = false;
            //}

            RuntimeCpu arch = RuntimeCpu.Unknown;
            var archPart = GetPart(RIDPart.Architecture);
            if (archPart != null && Enum.TryParse<RuntimeCpu>(archPart, true, out var parsed)) {
                arch = parsed;
            }

            var systemPart = GetPart(RIDPart.Base);
            RuntimeOs system = systemPart.ToLower() switch {
                "win" => RuntimeOs.Windows,
                "windows" => RuntimeOs.Windows,
                "linux" => RuntimeOs.Linux,
                "mac" => RuntimeOs.OSX,
                "osx" => RuntimeOs.OSX,
                "macos" => RuntimeOs.OSX,
                _ => RuntimeOs.Unknown,
            };

            return new RID() {
                BaseRID = system,
                //OmitVersionDelimiter = omitVersionDelimiter,
                Version = version == null ? null : new RuntimeVersion(version),
                Architecture = arch,
                Qualifier = GetPart(RIDPart.Qualifier)
            };
        }

        public bool HasVersion => Version != null;

        public bool HasArchitecture => Architecture != RuntimeCpu.Unknown;

        public bool HasQualifier => Qualifier != null;

        public bool IsValid => BaseRID != RuntimeOs.Unknown;

        public override bool Equals(object obj)
        {
            return Equals(obj as RID);
        }

        public bool Equals(RID obj)
        {
            return object.ReferenceEquals(obj, this) ||
                   (obj is not null &&
                    BaseRID == obj.BaseRID &&
                    //(Version == null || OmitVersionDelimiter == obj.OmitVersionDelimiter) &&
                    Version == obj.Version &&
                    Architecture == obj.Architecture &&
                    Qualifier == obj.Qualifier);
        }

        public override int GetHashCode()
        {
#if NETFRAMEWORK || NETSTANDARD
            return BaseRID.GetHashCode();
#else
            HashCode hashCode = default;
            hashCode.Add(BaseRID);
            hashCode.Add(Version);
            hashCode.Add(Architecture);
            hashCode.Add(Qualifier);
            return hashCode.ToHashCode();
#endif
        }
    }
}