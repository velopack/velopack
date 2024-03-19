using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Velopack
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
                    Utility.DeleteFileOrDirectoryHard(linkPath);
                } else {
                    throw new IOException("Junction / symlink path already exists and overwrite parameter is false.");
                }
            }

            var finalTarget = relative ? GetRelativePath(linkPath, targetPath) : targetPath;

            if (Directory.Exists(targetPath)) {
#if NETFRAMEWORK
                if (!CreateSymbolicLink(linkPath, finalTarget, SYMBOLIC_LINK_FLAG_DIRECTORY | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE))
                    ThrowLastWin32Error("Unable to create junction point / symlink.");
#else
                Directory.CreateSymbolicLink(linkPath, finalTarget);
#endif
            } else if (File.Exists(targetPath)) {
#if NETFRAMEWORK
                if (!CreateSymbolicLink(linkPath, finalTarget, SYMBOLIC_LINK_FLAG_FILE | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE))
                    ThrowLastWin32Error("Unable to create junction point / symlink.");
#else
                File.CreateSymbolicLink(linkPath, finalTarget);
#endif
            } else {
                throw new IOException("Target path does not exist.");
            }
        }

        public static bool Exists(string linkPath)
        {
            return TryGetLinkFsi(linkPath, out var _);
        }

        public static void Delete(string linkPath)
        {
            var isLink = TryGetLinkFsi(linkPath, out var fsi);
            if (fsi != null && !isLink) {
                throw new IOException("Path is not a junction point.");
            } else {
                fsi?.Delete();
            }
        }

        public static string GetTarget(string linkPath)
        {
            if (TryGetLinkFsi(linkPath, out var fsi)) {
                string target;
#if NETFRAMEWORK
                target = GetTargetWin32(linkPath);
#else
                target = fsi.LinkTarget!;
#endif
                if (Path.IsPathRooted(target)) {
                    // if the path is absolute, we can return it as is.
                    return Path.GetFullPath(target);
                } else {
                    // if it is a relative path, we need to resolve it as it relates to the location of linkPath
                    return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(linkPath)!, target));
                }
            }
            throw new IOException("Path does not exist or is not a junction point / symlink.");
        }

        public static string GetTargetRelativeToLink(string linkPath)
        {
            var targetPath = GetTarget(linkPath);
            return GetRelativePath(linkPath, targetPath);
        }

        private static bool TryGetLinkFsi(string path, out FileSystemInfo fsi)
        {
            fsi = null!;
            if (Directory.Exists(path)) {
                fsi = new DirectoryInfo(path);
            } else if (File.Exists(path)) {
                fsi = new FileInfo(path);
            }

            return fsi != null && (fsi.Attributes & FileAttributes.ReparsePoint) != 0;
        }

        private static string GetRelativePath(string relativeTo, string path)
        {
#if NETFRAMEWORK
            relativeTo = Path.GetFullPath(relativeTo);
            path = Path.GetFullPath(path);
            return ToggleRelative(relativeTo, path);
#else
            return Path.GetRelativePath(relativeTo, path);
#endif
        }

#if NETFRAMEWORK
        [Flags]
        private enum EFileAccess : uint
        {
            GenericRead = 0x80000000,
            GenericWrite = 0x40000000,
            GenericExecute = 0x20000000,
            GenericAll = 0x10000000,
        }

        [Flags]
        private enum EFileShare : uint
        {
            None = 0x00000000,
            Read = 0x00000001,
            Write = 0x00000002,
            Delete = 0x00000004,
        }

        private enum ECreationDisposition : uint
        {
            New = 1,
            CreateAlways = 2,
            OpenExisting = 3,
            OpenAlways = 4,
            TruncateExisting = 5,
        }

        [Flags]
        private enum EFileAttributes : uint
        {
            Readonly = 0x00000001,
            Hidden = 0x00000002,
            System = 0x00000004,
            Directory = 0x00000010,
            Archive = 0x00000020,
            Device = 0x00000040,
            Normal = 0x00000080,
            Temporary = 0x00000100,
            SparseFile = 0x00000200,
            ReparsePoint = 0x00000400,
            Compressed = 0x00000800,
            Offline = 0x00001000,
            NotContentIndexed = 0x00002000,
            Encrypted = 0x00004000,
            Write_Through = 0x80000000,
            Overlapped = 0x40000000,
            NoBuffering = 0x20000000,
            RandomAccess = 0x10000000,
            SequentialScan = 0x08000000,
            DeleteOnClose = 0x04000000,
            BackupSemantics = 0x02000000,
            PosixSemantics = 0x01000000,
            OpenReparsePoint = 0x00200000,
            OpenNoRecall = 0x00100000,
            FirstPipeInstance = 0x00080000
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            EFileAccess dwDesiredAccess,
            EFileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            ECreationDisposition dwCreationDisposition,
            EFileAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

        private const int SYMBOLIC_LINK_FLAG_FILE = 0x0;
        private const int SYMBOLIC_LINK_FLAG_DIRECTORY = 0x1;
        private const int SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE = 0x2;

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint GetFinalPathNameByHandle(IntPtr hFile, [MarshalAs(UnmanagedType.LPTStr)] StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

        private static string GetTargetWin32(string linkPath)
        {
            using var handle = new SafeFileHandle(CreateFile(linkPath, EFileAccess.GenericRead,
                EFileShare.Read | EFileShare.Write | EFileShare.Delete,
                IntPtr.Zero, ECreationDisposition.OpenExisting,
                EFileAttributes.BackupSemantics, IntPtr.Zero), true);

            if (Marshal.GetLastWin32Error() != 0)
                ThrowLastWin32Error("Unable to open reparse point.");

            var sb = new StringBuilder(1024);
            var res = GetFinalPathNameByHandle(handle.DangerousGetHandle(), sb, 1024, 0);
            if (res == 0)
                ThrowLastWin32Error("Unable to resolve reparse point target.");

            var result = sb.ToString();
            if (result.StartsWith(@"\\?\"))
                result = result.Substring(4);
            if (result.StartsWith(@"\\?\UNC\"))
                result = @"\\" + result.Substring(8);
            return result;
        }

        private static void ThrowLastWin32Error(string message)
        {
            throw new IOException(message, Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        }

        private static string ToggleRelative(string basePath, string toggledPath)
        {
            // from https://github.com/RT-Projects/RT.Util/blob/master/RT.Util.Core/Paths/PathUtil.cs#L297
            if (basePath.Length == 0)
                throw new Exception("InvalidBasePath");
            if (toggledPath.Length == 0)
                throw new Exception("InvalidToggledPath");
            if (!Path.IsPathRooted(basePath))
                throw new Exception("BasePathNotAbsolute");

            try { basePath = Path.GetFullPath(basePath + "\\"); } catch { throw new Exception("InvalidBasePath"); }

            if (!Path.IsPathRooted(toggledPath)) {
                try {
                    return StripTrailingSeparator(Path.GetFullPath(Path.Combine(basePath, toggledPath)));
                } catch {
                    throw new Exception("InvalidToggledPath");
                }
            }

            // Both basePath and toggledPath are absolute. Need to relativize toggledPath.
            try { toggledPath = Path.GetFullPath(toggledPath + "\\"); } catch { throw new Exception("InvalidToggledPath"); }
            int prevPos = -1;
            int pos = toggledPath.IndexOf(Path.DirectorySeparatorChar);
            while (pos != -1 && pos < basePath.Length && basePath.Substring(0, pos + 1).Equals(toggledPath.Substring(0, pos + 1), StringComparison.OrdinalIgnoreCase)) {
                prevPos = pos;
                pos = toggledPath.IndexOf(Path.DirectorySeparatorChar, pos + 1);
            }
            if (prevPos == -1)
                throw new Exception("PathsOnDifferentDrives");
            var piece = basePath.Substring(prevPos + 1);
            var result = StripTrailingSeparator((".." + Path.DirectorySeparatorChar).Repeat(piece.Count(ch => ch == Path.DirectorySeparatorChar))
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
