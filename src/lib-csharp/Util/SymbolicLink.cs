using System;
using System.IO;

namespace Velopack.Util
{
    internal static partial class SymbolicLink
    {
        /// <summary>
        /// Creates a symlink from the specified directory to the specified target directory.
        /// </summary>
        /// <param name="linkPath">The symlink path</param>
        /// <param name="targetPath">The target directory or file</param>
        /// <param name="overwrite">If true overwrites an existing reparse point or empty directory</param>
        /// <param name="relative">If true, stores a relative path from the link to the target, rather than an absolute path.</param>
        public static void Create(string linkPath, string targetPath, bool overwrite = true, bool relative = false)
        {
            linkPath = Path.GetFullPath(linkPath);
            targetPath = Path.GetFullPath(targetPath);

            if (!Directory.Exists(targetPath) && !File.Exists(targetPath)) {
                throw new IOException("Target path does not exist.");
            }

            if (Directory.Exists(linkPath) || File.Exists(linkPath)) {
                if (overwrite) {
                    IoUtil.DeleteFileOrDirectoryHard(linkPath);
                } else {
                    throw new IOException("Junction / symlink path already exists and overwrite parameter is false.");
                }
            }

            var finalTarget = relative
                ? PathUtil.MakePathRelativeTo(Path.GetDirectoryName(linkPath)!, targetPath)
                : targetPath;

            if (Directory.Exists(targetPath)) {
                CreateSymlink(linkPath, finalTarget, SymbolicLinkFlag.Directory);
            } else if (File.Exists(targetPath)) {
                CreateSymlink(linkPath, finalTarget, SymbolicLinkFlag.File);
            } else {
                throw new IOException("Target path does not exist.");
            }
        }

        /// <summary>
        /// Returns true if the specified path exists and is a junction point or symlink.
        /// If the path exists but is not a junction point or symlink, returns false.
        /// </summary>
        public static bool Exists(string linkPath)
        {
            return TryGetLinkFsi(linkPath, out var _);
        }

        /// <summary>
        /// Does nothing if the path does not exist. If the path exists but is not 
        /// a junction / symlink, throws an IOException.
        /// </summary>
        public static void Delete(string linkPath)
        {
            var isLink = TryGetLinkFsi(linkPath, out var fsi);
            if (fsi != null && !isLink) {
                throw new IOException("Path is not a junction point / symlink.");
            } else {
                fsi?.Delete();
            }
        }

        /// <summary>
        /// Get the target of a junction point or symlink.
        /// </summary>
        /// <param name="linkPath">The location of the symlink or junction point</param>
        /// <param name="relative">If true, the returned target path will be relative to the linkPath. Otherwise, it will be an absolute path.</param>
        public static string GetTarget(string linkPath, bool relative = false)
        {
            var target = GetUnresolvedTarget(linkPath);
            if (relative) {
                if (Path.IsPathRooted(target)) {
                    return PathUtil.MakePathRelativeTo(Path.GetDirectoryName(linkPath)!, target);
                } else {
                    return target;
                }
            } else {
                if (Path.IsPathRooted(target)) {
                    return Path.GetFullPath(target);
                } else {
                    return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(linkPath)!, target));
                }
            }
        }

        [Serializable]
        private enum SymbolicLinkFlag : uint
        {
            File = 0,
            Directory = 1,
        }

        private static void CreateSymlink(string linkPath, string targetPath, SymbolicLinkFlag mode)
        {
#if NET6_0_OR_GREATER
            if (mode == SymbolicLinkFlag.File) {
                File.CreateSymbolicLink(linkPath, targetPath);
            } else if (mode == SymbolicLinkFlag.Directory) {
                Directory.CreateSymbolicLink(linkPath, targetPath);
            } else {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid symbolic link mode.");
            }
#else
            if (VelopackRuntimeInfo.IsWindows) {
                WindowsCreateSymlink(targetPath, linkPath, mode);
            } else if (VelopackRuntimeInfo.IsLinux || VelopackRuntimeInfo.IsOSX) {
                UnixCreateSymlink(targetPath, linkPath);
            } else {
                throw new NotSupportedException("Symbolic links are not supported on this platform.");
            }
#endif
        }

        private static string GetUnresolvedTarget(string linkPath)
        {
            if (!TryGetLinkFsi(linkPath, out var fsi)) {
                throw new IOException("Path does not exist or is not a junction point / symlink.");
            }

#if NET6_0_OR_GREATER
            return fsi!.LinkTarget!;
#else
            if (VelopackRuntimeInfo.IsWindows) {
                return WindowsReadLink(linkPath);
            }

            if (VelopackRuntimeInfo.IsLinux || VelopackRuntimeInfo.IsOSX) {
                return UnixReadLink(linkPath);
            }

            throw new NotSupportedException();
#endif
        }

        private static bool TryGetLinkFsi(string path, out FileSystemInfo? fsi)
        {
            fsi = null;
            if (Directory.Exists(path)) {
                fsi = new DirectoryInfo(path);
            } else if (File.Exists(path)) {
                fsi = new FileInfo(path);
            }

            if (fsi == null) {
                return false;
            }

            return (fsi.Attributes & FileAttributes.ReparsePoint) != 0;
        }
    }
}