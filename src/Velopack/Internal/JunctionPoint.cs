#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Velopack
{
#if NET5_0_OR_GREATER
    public static class JunctionPoint
    {
        /// <summary>
        /// Creates a symlink from the specified directory to the specified target directory.
        /// </summary>
        /// <param name="junctionPoint">The symlink path</param>
        /// <param name="targetPath">The target directory or file</param>
        /// <param name="overwrite">If true overwrites an existing reparse point or empty directory</param>
        public static void Create(string junctionPoint, string targetPath, bool overwrite)
        {
            targetPath = Path.GetFullPath(targetPath);

            if (Directory.Exists(targetPath)) {
                if (Directory.Exists(junctionPoint)) {
                    if (!overwrite) {
                        throw new IOException("Directory already exists and overwrite parameter is false.");
                    }
                    Utility.DeleteFileOrDirectoryHard(junctionPoint);
                }
                Directory.CreateSymbolicLink(junctionPoint, targetPath);
            } else if (File.Exists(targetPath)) {
                if (File.Exists(junctionPoint)) {
                    if (!overwrite) {
                        throw new IOException("File already exists and overwrite parameter is false.");
                    }
                    Utility.DeleteFileOrDirectoryHard(junctionPoint);
                }
                File.CreateSymbolicLink(junctionPoint, targetPath);
            } else {
                throw new IOException("Target path does not exist.");
            }
        }

        public static bool Exists(string path)
        {
            if (Directory.Exists(path) && Directory.ResolveLinkTarget(path, true) != null) {
                return true;
            }
            if (File.Exists(path) && File.ResolveLinkTarget(path, true) != null) {
                return true;
            }
            return false;
        }

        public static void Delete(string junctionPoint)
        {
            if (File.Exists(junctionPoint)) {
                throw new IOException("Path is not a junction point.");
            }
            if (Directory.Exists(junctionPoint)) {
                if (Directory.ResolveLinkTarget(junctionPoint, true) != null) {
                    // DeleteFileOrDirectoryHard already has the logic to handle junction points
                    Utility.DeleteFileOrDirectoryHard(junctionPoint);
                    return;
                }
                throw new IOException("Path is not a junction point.");
            }
        }

        public static string GetTarget(string junctionPoint)
        {
            if (Directory.Exists(junctionPoint)) {
                var target = Directory.ResolveLinkTarget(junctionPoint, true);
                if (target != null) {
                    return target.FullName;
                }
            } else if (File.Exists(junctionPoint)) {
                var target = File.ResolveLinkTarget(junctionPoint, true);
                if (target != null) {
                    return target.FullName;
                }
            }
            throw new IOException("Path is not a junction point.");
        }

        /// <summary>
        /// Create a relative path from one path to another. Paths will be resolved before calculating the difference.
        /// Default path comparison for the active platform will be used (OrdinalIgnoreCase for Windows or Mac, Ordinal for Unix).
        /// </summary>
        /// <param name="relativeTo">The source path the output should be relative to. This path is always considered to be a directory.</param>
        /// <param name="path">The destination path.</param>
        /// <returns>The relative path or <paramref name="path"/> if the paths don't share the same root.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="relativeTo"/> or <paramref name="path"/> is <c>null</c> or an empty string.</exception>
        public static string GetRelativePath(string relativeTo, string path)
        {
            return Path.GetRelativePath(relativeTo, path);
        }
    }
#else
    /// <summary>
    /// Provides access to NTFS junction points in .Net. From https://www.codeproject.com/script/Articles/ViewDownloads.aspx?aid=15633
    /// </summary>
    public static class JunctionPoint
    {
        /// <summary>
        /// The file or directory is not a reparse point.
        /// </summary>
        private const int ERROR_NOT_A_REPARSE_POINT = 4390;

        /// <summary>
        /// The reparse point attribute cannot be set because it conflicts with an existing attribute.
        /// </summary>
        private const int ERROR_REPARSE_ATTRIBUTE_CONFLICT = 4391;

        /// <summary>
        /// The data present in the reparse point buffer is invalid.
        /// </summary>
        private const int ERROR_INVALID_REPARSE_DATA = 4392;

        /// <summary>
        /// The tag present in the reparse point buffer is invalid.
        /// </summary>
        private const int ERROR_REPARSE_TAG_INVALID = 4393;

        /// <summary>
        /// There is a mismatch between the tag specified in the request and the tag present in the reparse point.
        /// </summary>
        private const int ERROR_REPARSE_TAG_MISMATCH = 4394;

        /// <summary>
        /// Command to set the reparse point data block.
        /// </summary>
        private const int FSCTL_SET_REPARSE_POINT = 0x000900A4;

        /// <summary>
        /// Command to get the reparse point data block.
        /// </summary>
        private const int FSCTL_GET_REPARSE_POINT = 0x000900A8;

        /// <summary>
        /// Command to delete the reparse point data base.
        /// </summary>
        private const int FSCTL_DELETE_REPARSE_POINT = 0x000900AC;

        /// <summary>
        /// Reparse point tag used to identify mount points and junction points.
        /// </summary>
        private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;

        /// <summary>
        /// This prefix indicates to NTFS that the path is to be treated as a non-interpreted
        /// path in the virtual file system.
        /// </summary>
        private const string NonInterpretedPathPrefix = @"\??\";

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

        [StructLayout(LayoutKind.Sequential)]
        private struct REPARSE_DATA_BUFFER
        {
            /// <summary>
            /// Reparse point tag. Must be a Microsoft reparse point tag.
            /// </summary>
            public uint ReparseTag;

            /// <summary>
            /// Size, in bytes, of the data after the Reserved member. This can be calculated by:
            /// (4 * sizeof(ushort)) + SubstituteNameLength + PrintNameLength + 
            /// (namesAreNullTerminated ? 2 * sizeof(char) : 0);
            /// </summary>
            public ushort ReparseDataLength;

            /// <summary>
            /// Reserved; do not use. 
            /// </summary>
            public ushort Reserved;

            /// <summary>
            /// Offset, in bytes, of the substitute name string in the PathBuffer array.
            /// </summary>
            public ushort SubstituteNameOffset;

            /// <summary>
            /// Length, in bytes, of the substitute name string. If this string is null-terminated,
            /// SubstituteNameLength does not include space for the null character.
            /// </summary>
            public ushort SubstituteNameLength;

            /// <summary>
            /// Offset, in bytes, of the print name string in the PathBuffer array.
            /// </summary>
            public ushort PrintNameOffset;

            /// <summary>
            /// Length, in bytes, of the print name string. If this string is null-terminated,
            /// PrintNameLength does not include space for the null character. 
            /// </summary>
            public ushort PrintNameLength;

            /// <summary>
            /// A buffer containing the unicode-encoded path string. The path string contains
            /// the substitute name string and print name string.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x3FF0)]
            public byte[] PathBuffer;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
            IntPtr InBuffer, int nInBufferSize,
            IntPtr OutBuffer, int nOutBufferSize,
            out int pBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            EFileAccess dwDesiredAccess,
            EFileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            ECreationDisposition dwCreationDisposition,
            EFileAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        /// <summary>
        /// Creates a junction point from the specified directory to the specified target directory.
        /// </summary>
        /// <remarks>
        /// Only works on NTFS.
        /// </remarks>
        /// <param name="junctionPoint">The junction point path</param>
        /// <param name="targetDir">The target directory</param>
        /// <param name="overwrite">If true overwrites an existing reparse point or empty directory</param>
        /// <exception cref="IOException">Thrown when the junction point could not be created or when
        /// an existing directory was found and <paramref name="overwrite" /> if false</exception>
        public static void Create(string junctionPoint, string targetDir, bool overwrite)
        {
            targetDir = Path.GetFullPath(targetDir);

            if (!Directory.Exists(targetDir))
                throw new IOException("Target path does not exist or is not a directory.");

            if (Directory.Exists(junctionPoint)) {
                if (!overwrite)
                    throw new IOException("Directory already exists and overwrite parameter is false.");
            } else {
                Directory.CreateDirectory(junctionPoint);
            }

            using (SafeFileHandle handle = OpenReparsePoint(junctionPoint, EFileAccess.GenericWrite)) {
                byte[] targetDirBytes = Encoding.Unicode.GetBytes(NonInterpretedPathPrefix + Path.GetFullPath(targetDir));

                REPARSE_DATA_BUFFER reparseDataBuffer = new REPARSE_DATA_BUFFER();

                reparseDataBuffer.ReparseTag = IO_REPARSE_TAG_MOUNT_POINT;
                reparseDataBuffer.ReparseDataLength = (ushort) (targetDirBytes.Length + 12);
                reparseDataBuffer.SubstituteNameOffset = 0;
                reparseDataBuffer.SubstituteNameLength = (ushort) targetDirBytes.Length;
                reparseDataBuffer.PrintNameOffset = (ushort) (targetDirBytes.Length + 2);
                reparseDataBuffer.PrintNameLength = 0;
                reparseDataBuffer.PathBuffer = new byte[0x3ff0];
                Array.Copy(targetDirBytes, reparseDataBuffer.PathBuffer, targetDirBytes.Length);

                int inBufferSize = Marshal.SizeOf(reparseDataBuffer);
                IntPtr inBuffer = Marshal.AllocHGlobal(inBufferSize);

                try {
                    Marshal.StructureToPtr(reparseDataBuffer, inBuffer, false);

                    int bytesReturned;
                    bool result = DeviceIoControl(handle.DangerousGetHandle(), FSCTL_SET_REPARSE_POINT,
                        inBuffer, targetDirBytes.Length + 20, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

                    if (!result)
                        ThrowLastWin32Error("Unable to create junction point.");
                } finally {
                    Marshal.FreeHGlobal(inBuffer);
                }
            }
        }

        /// <summary>
        /// Deletes a junction point at the specified source directory along with the directory itself.
        /// Does nothing if the junction point does not exist.
        /// </summary>
        /// <remarks>
        /// Only works on NTFS.
        /// </remarks>
        /// <param name="junctionPoint">The junction point path</param>
        public static void Delete(string junctionPoint)
        {
            if (!Directory.Exists(junctionPoint)) {
                if (File.Exists(junctionPoint))
                    throw new IOException("Path is not a junction point.");

                return;
            }

            using (SafeFileHandle handle = OpenReparsePoint(junctionPoint, EFileAccess.GenericWrite)) {
                REPARSE_DATA_BUFFER reparseDataBuffer = new REPARSE_DATA_BUFFER();

                reparseDataBuffer.ReparseTag = IO_REPARSE_TAG_MOUNT_POINT;
                reparseDataBuffer.ReparseDataLength = 0;
                reparseDataBuffer.PathBuffer = new byte[0x3ff0];

                int inBufferSize = Marshal.SizeOf(reparseDataBuffer);
                IntPtr inBuffer = Marshal.AllocHGlobal(inBufferSize);
                try {
                    Marshal.StructureToPtr(reparseDataBuffer, inBuffer, false);

                    int bytesReturned;
                    bool result = DeviceIoControl(handle.DangerousGetHandle(), FSCTL_DELETE_REPARSE_POINT,
                        inBuffer, 8, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

                    if (!result)
                        ThrowLastWin32Error("Unable to delete junction point.");
                } finally {
                    Marshal.FreeHGlobal(inBuffer);
                }

                try {
                    Directory.Delete(junctionPoint);
                } catch (IOException ex) {
                    throw new IOException("Unable to delete junction point.", ex);
                }
            }
        }

        /// <summary>
        /// Determines whether the specified path exists and refers to a junction point.
        /// </summary>
        /// <param name="path">The junction point path</param>
        /// <returns>True if the specified path represents a junction point</returns>
        /// <exception cref="IOException">Thrown if the specified path is invalid
        /// or some other error occurs</exception>
        public static bool Exists(string path)
        {
            if (!Directory.Exists(path))
                return false;

            using (SafeFileHandle handle = OpenReparsePoint(path, EFileAccess.GenericRead)) {
                string? target = InternalGetTarget(handle);
                return target != null;
            }
        }

        /// <summary>
        /// Gets the target of the specified junction point.
        /// </summary>
        /// <remarks>
        /// Only works on NTFS.
        /// </remarks>
        /// <param name="junctionPoint">The junction point path</param>
        /// <returns>The target of the junction point</returns>
        /// <exception cref="IOException">Thrown when the specified path does not
        /// exist, is invalid, is not a junction point, or some other error occurs</exception>
        public static string GetTarget(string junctionPoint)
        {
            using (SafeFileHandle handle = OpenReparsePoint(junctionPoint, EFileAccess.GenericRead)) {
                string? target = InternalGetTarget(handle);
                if (target == null)
                    throw new IOException("Path is not a junction point.");

                return target;
            }
        }

        private static string? InternalGetTarget(SafeFileHandle handle)
        {
            int outBufferSize = Marshal.SizeOf(typeof(REPARSE_DATA_BUFFER));
            IntPtr outBuffer = Marshal.AllocHGlobal(outBufferSize);

            try {
                int bytesReturned;
                bool result = DeviceIoControl(handle.DangerousGetHandle(), FSCTL_GET_REPARSE_POINT,
                    IntPtr.Zero, 0, outBuffer, outBufferSize, out bytesReturned, IntPtr.Zero);

                if (!result) {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ERROR_NOT_A_REPARSE_POINT)
                        return null;

                    ThrowLastWin32Error("Unable to get information about junction point.");
                }

                var reparseObj = Marshal.PtrToStructure(outBuffer, typeof(REPARSE_DATA_BUFFER));
                if (reparseObj == null)
                    return null;

                REPARSE_DATA_BUFFER reparseDataBuffer = (REPARSE_DATA_BUFFER) reparseObj;

                if (reparseDataBuffer.ReparseTag != IO_REPARSE_TAG_MOUNT_POINT)
                    return null;

                string targetDir = Encoding.Unicode.GetString(reparseDataBuffer.PathBuffer,
                    reparseDataBuffer.SubstituteNameOffset, reparseDataBuffer.SubstituteNameLength);

                if (targetDir.StartsWith(NonInterpretedPathPrefix))
                    targetDir = targetDir.Substring(NonInterpretedPathPrefix.Length);

                return targetDir;
            } finally {
                Marshal.FreeHGlobal(outBuffer);
            }
        }

        private static SafeFileHandle OpenReparsePoint(string reparsePoint, EFileAccess accessMode)
        {
            SafeFileHandle reparsePointHandle = new SafeFileHandle(CreateFile(reparsePoint, accessMode,
                EFileShare.Read | EFileShare.Write | EFileShare.Delete,
                IntPtr.Zero, ECreationDisposition.OpenExisting,
                EFileAttributes.BackupSemantics | EFileAttributes.OpenReparsePoint, IntPtr.Zero), true);

            if (Marshal.GetLastWin32Error() != 0)
                ThrowLastWin32Error("Unable to open reparse point.");

            return reparsePointHandle;
        }

        private static void ThrowLastWin32Error(string message)
        {
            throw new IOException(message, Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        }

        /// <summary>
        /// Create a relative path from one path to another. Paths will be resolved before calculating the difference.
        /// Default path comparison for the active platform will be used (OrdinalIgnoreCase for Windows or Mac, Ordinal for Unix).
        /// </summary>
        /// <param name="relativeTo">The source path the output should be relative to. This path is always considered to be a directory.</param>
        /// <param name="path">The destination path.</param>
        /// <returns>The relative path or <paramref name="path"/> if the paths don't share the same root.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="relativeTo"/> or <paramref name="path"/> is <c>null</c> or an empty string.</exception>
        public static string GetRelativePath(string relativeTo, string path)
        {
            relativeTo = Path.GetFullPath(relativeTo);
            path = Path.GetFullPath(path);
            return ToggleRelative(relativeTo, path);
        }

        /// <summary>
        ///     Changes a relative <paramref name="toggledPath"/> to an absolute and vice versa, with respect to <paramref
        ///     name="basePath"/>. Neither path must be an empty string. Any trailing slashes are ignored and the result won't
        ///     have one except for root "C:\"-style paths. Forward slashes, multiple repeated slashes, and any redundant "." or
        ///     ".." elements are correctly interpreted and eliminated. See Remarks for some special cases.</summary>
        /// <remarks>
        ///     Relative paths that specify a drive letter "C:thing" are not supported and result in undefined behaviour. If the
        ///     toggled path is relative then all ".." levels that expand beyond the root directory are silently discarded.</remarks>
        /// <param name="basePath">
        ///     An absolute path to the directory which serves as the base for absolute/relative conversion.</param>
        /// <param name="toggledPath">
        ///     An absolute or a relative path to be converted.</param>
        /// <returns>
        ///     The converted path.</returns>
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

            if (!Path.IsPathRooted(toggledPath))
                try {
                    return StripTrailingSeparator(Path.GetFullPath(Path.Combine(basePath, toggledPath)));
                } catch {
                    throw new Exception("InvalidToggledPath");
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

        /// <summary>
        ///     Concatenates the specified number of repetitions of the current string.</summary>
        /// <param name="input">
        ///     The string to be repeated.</param>
        /// <param name="numTimes">
        ///     The number of times to repeat the string.</param>
        /// <returns>
        ///     A concatenated string containing the original string the specified number of times.</returns>
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

        /// <summary>
        ///     Strips a single trailing directory separator, whether it's the forward- or backslash. Preserves the single
        ///     separator at the end of paths referring to the root of a drive, such as "C:\". Removes at most a single separator,
        ///     never more.</summary>
        private static string StripTrailingSeparator(string path)
        {
            if (path.Length < 1)
                return path;
            if (path[path.Length - 1] == '/' || path[path.Length - 1] == '\\')
                return (path.Length == 3 && path[1] == ':') ? path : path.Substring(0, path.Length - 1);
            else
                return path;
        }
    }
#endif
}