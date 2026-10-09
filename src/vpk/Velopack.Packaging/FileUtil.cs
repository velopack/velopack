using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging;

public static class FileUtil
{
    public static void CopyDirectoryContents(string source, string dest, ILogger logger = null)
    {
        if (!Directory.Exists(source)) {
            throw new ArgumentException("Source directory does not exist: " + source);
        }

        if (!Directory.Exists(dest)) {
            Directory.CreateDirectory(dest);
        }

        var sourceRoot = Path.GetFullPath(source);
        ValidateSymlinks(sourceRoot);

        if (VelopackRuntimeInfo.IsWindows) {
            logger?.LogDebug("Copying '{Source}' to '{Dest}' (built-in recursive)", source, dest);
            CopyFilesRecursive(new DirectoryInfo(sourceRoot), new DirectoryInfo(dest), preserveSymlinks: true);
        } else {
            logger?.LogDebug("Copying '{Source}' to '{Dest}' (preserving symlinks via cp)", source, dest);
            var src = source.TrimEnd('/') + "/.";
            var des = dest.TrimEnd('/') + "/";
            Exe.InvokeAndThrowIfNonZero("cp", ["-a", src, des], null);
        }
    }

    /// <summary>
    /// Throws a <see cref="UserInfoException"/> if any symlink under <paramref name="sourceRoot"/> resolves to a path outside of it.
    /// Symlinked directories are checked but never descended into.
    /// </summary>
    /// <param name="shouldSkipFile">Receives the path of each file symlink relative to <paramref name="sourceRoot"/>; returning
    /// true leaves that link unchecked, for links that will be excluded from the copy anyway.</param>
    public static void ValidateSymlinks(string sourceRoot, Func<string, bool> shouldSkipFile = null)
    {
        sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        ValidateSymlinks(new DirectoryInfo(sourceRoot), sourceRoot, shouldSkipFile);
    }

    /// <summary>
    /// Creates a symlink at <paramref name="linkPath"/> storing the path of the existing <paramref name="targetPath"/> relative
    /// to it, replacing anything already at <paramref name="linkPath"/>. A refused symlink privilege is reported as a
    /// <see cref="UserInfoException"/> that explains how to allow it.
    /// </summary>
    public static void CreateRelativeSymlink(string linkPath, string targetPath)
    {
        ReportRefusedSymlinkPrivilege(() => SymbolicLink.Create(linkPath, targetPath, overwrite: true, relative: true));
    }

    /// <summary>
    /// Enumerates every file under <paramref name="directory"/>, file symlinks included, without descending into symlinked
    /// directories.
    /// </summary>
    public static IEnumerable<FileInfo> EnumerateFilesWithoutFollowingSymlinks(DirectoryInfo directory)
    {
        foreach (var file in directory.EnumerateFiles()) {
            yield return file;
        }

        foreach (var subDir in directory.EnumerateDirectories()) {
            if (subDir.LinkTarget == null) {
                foreach (var file in EnumerateFilesWithoutFollowingSymlinks(subDir)) {
                    yield return file;
                }
            }
        }
    }

    /// <summary>
    /// Recursively copies the contents of <paramref name="source"/> into <paramref name="target"/>. With
    /// <paramref name="preserveSymlinks"/>, symlinks are recreated as symlinks and never followed (run
    /// <see cref="ValidateSymlinks(string, Func{string, bool})"/> on the source first); without it, the files they point to are copied instead.
    /// </summary>
    /// <param name="shouldSkipFile">Receives the path of each file relative to <paramref name="source"/>; returning true leaves
    /// that file out.</param>
    /// <param name="progress">Reports 0-100 as files are copied.</param>
    public static void CopyFilesRecursive(DirectoryInfo source, DirectoryInfo target, bool preserveSymlinks,
        Func<string, bool> shouldSkipFile = null, Action<int> progress = null)
    {
        var sourceRoot = Path.TrimEndingDirectorySeparator(source.FullName);
        var totalFiles = progress == null ? 0 : Math.Max(1, EnumerateFilesWithoutFollowingSymlinks(source).Count());
        var currentFile = 0;

        CopyDirectory(source, target, "");
        progress?.Invoke(100);

        void CopyDirectory(DirectoryInfo sourceDir, DirectoryInfo targetDir, string relativeDir)
        {
            foreach (var fileInfo in sourceDir.GetFiles()) {
                var destPath = Path.Combine(targetDir.FullName, fileInfo.Name);
                progress?.Invoke(Math.Min(100, (int) ((double) ++currentFile / totalFiles * 100)));
                if (shouldSkipFile?.Invoke(Path.Combine(relativeDir, fileInfo.Name)) == true) {
                    continue;
                }

                if (preserveSymlinks && fileInfo.LinkTarget != null) {
                    CopySymlink(fileInfo, destPath, sourceRoot, isDirectory: false);
                } else {
                    fileInfo.CopyTo(destPath, true);
                    File.SetLastWriteTimeUtc(destPath, fileInfo.LastWriteTimeUtc);
                    File.SetLastAccessTimeUtc(destPath, fileInfo.LastAccessTimeUtc);
                    File.SetCreationTimeUtc(destPath, fileInfo.CreationTimeUtc);
                }
            }

            foreach (var sourceSubDir in sourceDir.GetDirectories()) {
                var destPath = Path.Combine(targetDir.FullName, sourceSubDir.Name);
                if (preserveSymlinks && sourceSubDir.LinkTarget != null) {
                    CopySymlink(sourceSubDir, destPath, sourceRoot, isDirectory: true);
                } else {
                    var targetSubDir = targetDir.CreateSubdirectory(sourceSubDir.Name);
                    CopyDirectory(sourceSubDir, targetSubDir, Path.Combine(relativeDir, sourceSubDir.Name));
                    Directory.SetLastWriteTimeUtc(targetSubDir.FullName, sourceSubDir.LastWriteTimeUtc);
                    Directory.SetLastAccessTimeUtc(targetSubDir.FullName, sourceSubDir.LastAccessTimeUtc);
                    Directory.SetCreationTimeUtc(targetSubDir.FullName, sourceSubDir.CreationTimeUtc);
                }
            }
        }
    }

    private static void CopySymlink(FileSystemInfo link, string destPath, string sourceRoot, bool isDirectory)
    {
        var linkTarget = link.LinkTarget!;
        if (Path.IsPathRooted(linkTarget)) {
            // An absolute target (e.g. a Windows junction) would keep pointing into the source tree, so re-point it at the copy.
            var resolvedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(linkTarget));
            if (IsInsideDirectory(resolvedTarget, sourceRoot)) {
                linkTarget = Path.GetRelativePath(Path.GetDirectoryName(link.FullName)!, resolvedTarget);
            }
        }

        ReportRefusedSymlinkPrivilege(() => SymbolicLink.CreateWithTarget(destPath, linkTarget, isDirectory));
    }

    /// <summary>
    /// <see cref="SymbolicLink"/> reports a refused symlink privilege (e.g. Windows without Developer Mode or elevation) as an
    /// <see cref="UnauthorizedAccessException"/> whose message says how to allow it; show that message without a stack trace.
    /// </summary>
    private static void ReportRefusedSymlinkPrivilege(Action createSymlink)
    {
        try {
            createSymlink();
        } catch (UnauthorizedAccessException ex) {
            throw new UserInfoException(ex.Message, ex);
        }
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        return path.Equals(directory, VelopackRuntimeInfo.PathStringComparison)
            || path.StartsWith(directory + Path.DirectorySeparatorChar, VelopackRuntimeInfo.PathStringComparison);
    }

    private static void ValidateSymlinkTarget(string linkPath, string linkTarget, string sourceRoot)
    {
        var resolvedTarget = Path.IsPathRooted(linkTarget)
            ? Path.GetFullPath(linkTarget)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(linkPath)!, linkTarget));

        if (!IsInsideDirectory(Path.TrimEndingDirectorySeparator(resolvedTarget), sourceRoot)) {
            throw new UserInfoException(
                $"Symlink '{linkPath}' points to '{linkTarget}' which resolves outside the source directory. " +
                "Only internal symlinks are allowed.");
        }
    }

    private static void ValidateSymlinks(DirectoryInfo dir, string sourceRoot, Func<string, bool> shouldSkipFile)
    {
        foreach (var file in dir.GetFiles()) {
            if (file.LinkTarget != null && shouldSkipFile?.Invoke(Path.GetRelativePath(sourceRoot, file.FullName)) != true) {
                ValidateSymlinkTarget(file.FullName, file.LinkTarget, sourceRoot);
            }
        }

        foreach (var subDir in dir.GetDirectories()) {
            if (subDir.LinkTarget != null) {
                ValidateSymlinkTarget(subDir.FullName, subDir.LinkTarget, sourceRoot);
            } else {
                ValidateSymlinks(subDir, sourceRoot, shouldSkipFile);
            }
        }
    }
}
