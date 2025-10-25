using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Velopack.Util
{
    internal static class PathUtil
    {
        public static string MakePathRelativeTo(string relativeTo, string thePath)
        {
#if NETFRAMEWORK || NETSTANDARD
            relativeTo = Path.GetFullPath(relativeTo);
            thePath = Path.GetFullPath(thePath);
            return ToggleRelative(relativeTo, thePath);
#else
            return Path.GetRelativePath(relativeTo, thePath);
#endif
        }

        public static bool FullPathEquals(string path1, string path2)
        {
            return NormalizePath(path1).Equals(NormalizePath(path2), VelopackRuntimeInfo.PathStringComparison);
        }

        public static bool PathPartEquals(string part1, string part2)
        {
            return part1.Equals(part2, VelopackRuntimeInfo.PathStringComparison);
        }

        public static bool PathPartStartsWith(string part1, string startsWith)
        {
            return part1.StartsWith(startsWith, VelopackRuntimeInfo.PathStringComparison);
        }

        public static bool PathPartEndsWith(string part1, string endsWith)
        {
            return part1.EndsWith(endsWith, VelopackRuntimeInfo.PathStringComparison);
        }

        public static bool FileHasExtension(string filePath, string extension)
        {
            var ext = Path.GetExtension(filePath);
            if (!extension.StartsWith(".")) extension = "." + extension;
            return PathPartEquals(ext, extension);
        }

        public static string NormalizePath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var normalized = new Uri(fullPath, UriKind.Absolute).LocalPath;
            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static bool IsFileInDirectory(string file, string directory)
        {
            var normalizedDir = NormalizePath(directory) + Path.DirectorySeparatorChar;
            var normalizedFile = NormalizePath(file);
            return normalizedFile.StartsWith(normalizedDir, VelopackRuntimeInfo.PathStringComparison);
        }

        /// <summary>
        /// Escapes file name such that the file name is safe for writing to disk in the packages folder
        /// </summary>
        public static string GetSafeFilename(string fileName)
        {
            string safeFileName = Path.GetFileName(fileName);
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();

            if (safeFileName.IndexOfAny(invalidFileNameChars) != -1) {
                StringBuilder safeName = new();
                foreach (char ch in safeFileName) {
                    if (Array.IndexOf(invalidFileNameChars, ch) == -1)
                        safeName.Append(ch);
                    else
                        safeName.Append('_');
                }

                safeFileName = safeName.ToString();
            }

            return safeFileName;
        }

        private static readonly string[] peExtensions = new[] { ".exe", ".dll", ".node" };

        public static bool FileIsLikelyPEImage(string name)
        {
            var ext = Path.GetExtension(name);
            return peExtensions.Any(x => ext.Equals(x, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsDirectoryWritable(string directoryPath)
        {
            try {
                using var fs = File.Create(Path.Combine(directoryPath, ".velopack_dir_test"), 1, FileOptions.DeleteOnClose);
                return true;
            } catch {
                return false;
            }
        }

#if NETFRAMEWORK || NETSTANDARD
        private static string ToggleRelative(string basePath, string toggledPath)
        {
            // from https://github.com/RT-Projects/RT.Util/blob/master/RT.Util.Core/Paths/PathUtil.cs#L297
            if (basePath.Length == 0)
                throw new Exception("InvalidBasePath");
            if (toggledPath.Length == 0)
                throw new Exception("InvalidToggledPath");
            if (!Path.IsPathRooted(basePath))
                throw new Exception("BasePathNotAbsolute");

            try { basePath = Path.GetFullPath(basePath + Path.DirectorySeparatorChar); } catch { throw new Exception("InvalidBasePath"); }

            if (!Path.IsPathRooted(toggledPath)) {
                try {
                    return StripTrailingSeparator(Path.GetFullPath(Path.Combine(basePath, toggledPath)));
                } catch {
                    throw new Exception("InvalidToggledPath");
                }
            }

            // Both basePath and toggledPath are absolute. Need to relativize toggledPath.
            try { toggledPath = Path.GetFullPath(toggledPath + Path.DirectorySeparatorChar); } catch { throw new Exception("InvalidToggledPath"); }

            int prevPos = -1;
            int pos = toggledPath.IndexOf(Path.DirectorySeparatorChar);
            while (pos != -1 && pos < basePath.Length &&
                   basePath.Substring(0, pos + 1).Equals(toggledPath.Substring(0, pos + 1), StringComparison.OrdinalIgnoreCase)) {
                prevPos = pos;
                pos = toggledPath.IndexOf(Path.DirectorySeparatorChar, pos + 1);
            }

            if (prevPos == -1)
                throw new Exception("PathsOnDifferentDrives");
            var piece = basePath.Substring(prevPos + 1);
            var result = StripTrailingSeparator(
                (".." + Path.DirectorySeparatorChar).Repeat(piece.Count(ch => ch == Path.DirectorySeparatorChar))
                + toggledPath.Substring(prevPos + 1));
            return result.Length == 0 ? "." : result;
        }

        private static string Repeat(this string input, int numTimes)
        {
            if (numTimes == 0) return "";
            if (numTimes == 1) return input;
            if (numTimes == 2) return input + input;
            var sb = new StringBuilder();
            for (int i = 0; i < numTimes; i++)
                sb.Append(input);
            return sb.ToString();
        }

        private static string StripTrailingSeparator(string path)
        {
            if (path.Length < 1)
                return path;
            if (path[path.Length - 1] == '/' || path[path.Length - 1] == '\\')
                return (path.Length == 3 && path[1] == ':') ? path : path.Substring(0, path.Length - 1);
            else
                return path;
        }
#endif
    }
}