#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.IO.Compression;

namespace Squirrel.NuGet
{
    public static class ZipExtensions
    {
        public static bool IsDirectory(this ZipArchiveEntry entry)
        {
            return entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\") || String.IsNullOrEmpty(entry.Name);
        }
    }
}
