using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using NCode.ReparsePoints;

namespace Velopack.Util
{
    internal static class SymbolicLink
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
                CreateDirectoryLink(linkPath, finalTarget, targetPath);
            } else if (File.Exists(targetPath)) {
                CreateFileLink(linkPath, finalTarget);
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

        private static void CreateFileLink(string linkPath, string targetPath)
        {
            if (VelopackRuntimeInfo.IsWindows) {
                var rp = new ReparsePointProvider();
                rp.CreateSymbolicLink(linkPath, targetPath, false);
            } else {
#if NETSTANDARD
                var fileInfo = new Mono.Unix.UnixFileInfo(targetPath);
                fileInfo.CreateSymbolicLink(linkPath);
#elif NET6_0_OR_GREATER
                File.CreateSymbolicLink(linkPath, targetPath);
#else
                throw new NotSupportedException();
#endif
            }
        }

        private static void CreateDirectoryLink(string linkPath, string targetPath, string absoluteTargetPath)
        {
            if (VelopackRuntimeInfo.IsWindows) {
                var rp = new ReparsePointProvider();
                try {
                    rp.CreateSymbolicLink(linkPath, targetPath, true);
                } catch (Win32Exception ex) when (ex.NativeErrorCode == 1314) {
                    // on windows 10 and below, symbolic links can only be created by an administrator
                    // junctions also do not support relative target path's
                    rp.CreateJunction(linkPath, absoluteTargetPath);
                }
            } else {
#if NETSTANDARD
                var linkInfo = new Mono.Unix.UnixSymbolicLinkInfo(linkPath);
                linkInfo.CreateSymbolicLinkTo(targetPath);
#elif NET6_0_OR_GREATER
                Directory.CreateSymbolicLink(linkPath, targetPath);
#else
                throw new NotSupportedException();
#endif
            }
        }

        private static string GetUnresolvedTarget(string linkPath)
        {
            if (!TryGetLinkFsi(linkPath, out var fsi)) {
                throw new IOException("Path does not exist or is not a junction point / symlink.");
            }

            if (VelopackRuntimeInfo.IsWindows) {
                var rp = new ReparsePointProvider();
                var link = rp.GetLink(linkPath);
                return link.Target;
            } else {
#if NETSTANDARD
                return Mono.Unix.UnixPath.ReadLink(linkPath);;
#elif NET6_0_OR_GREATER
                return fsi!.LinkTarget!;
#else
                throw new NotSupportedException();
#endif
            }
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