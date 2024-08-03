﻿#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;

namespace Velopack.NuGet
{
    public class ZipPackageFile : IEquatable<ZipPackageFile>
    {
        public Uri Key { get; }
        public string EffectivePath { get; }
        public string TargetFramework { get; }
        public string Path { get; }

        public ZipPackageFile(Uri relpath)
        {
            Key = relpath;
            Path = NugetUtil.GetPath(relpath);
            TargetFramework = NugetUtil.ParseFrameworkNameFromFilePath(Path, out var effectivePath);
            EffectivePath = effectivePath;
        }

        public bool IsLibFile() => IsFileInTopDirectory(NugetUtil.LibDirectory);
        public bool IsContentFile() => IsFileInTopDirectory(NugetUtil.ContentDirectory);

        public bool IsFileInTopDirectory(string directory)
        {
            string folderPrefix = directory + System.IO.Path.DirectorySeparatorChar;
            return Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString() => Path;

        public override int GetHashCode() => Path.GetHashCode();

        public override bool Equals(object? obj)
        {
            if (obj is ZipPackageFile zpf)
                return Equals(zpf);
            return false;
        }

        public bool Equals(ZipPackageFile? other)
        {
            if (other == null) return false;
            return Path.Equals(other.Path);
        }
    }
}