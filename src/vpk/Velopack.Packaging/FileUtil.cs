using Microsoft.Extensions.Logging;
using Velopack.Core;

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
        ValidateSymlinks(new DirectoryInfo(sourceRoot), sourceRoot);

        if (VelopackRuntimeInfo.IsWindows) {
            logger?.LogDebug("Copying '{Source}' to '{Dest}' (built-in recursive)", source, dest);
            CopyFilesRecursive(new DirectoryInfo(sourceRoot), new DirectoryInfo(dest));
        } else {
            logger?.LogDebug("Copying '{Source}' to '{Dest}' (preserving symlinks via cp)", source, dest);
            var src = source.TrimEnd('/') + "/.";
            var des = dest.TrimEnd('/') + "/";
            Exe.InvokeAndThrowIfNonZero("cp", ["-a", src, des], null);
        }
    }

    private static void ValidateSymlinkTarget(string linkPath, string linkTarget, string sourceRoot)
    {
        var resolvedTarget = Path.IsPathRooted(linkTarget)
            ? Path.GetFullPath(linkTarget)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(linkPath)!, linkTarget));

        if (!resolvedTarget.StartsWith(sourceRoot + Path.DirectorySeparatorChar) && resolvedTarget != sourceRoot) {
            throw new UserInfoException(
                $"Symlink '{linkPath}' points to '{linkTarget}' which resolves outside the source directory. " +
                "Only internal symlinks are allowed.");
        }
    }

    private static void ValidateSymlinks(DirectoryInfo dir, string sourceRoot)
    {
        foreach (var file in dir.GetFiles()) {
            if (file.LinkTarget != null) {
                ValidateSymlinkTarget(file.FullName, file.LinkTarget, sourceRoot);
            }
        }

        foreach (var subDir in dir.GetDirectories()) {
            if (subDir.LinkTarget != null) {
                ValidateSymlinkTarget(subDir.FullName, subDir.LinkTarget, sourceRoot);
            } else {
                ValidateSymlinks(subDir, sourceRoot);
            }
        }
    }

    private static void CopyFilesRecursive(DirectoryInfo source, DirectoryInfo target)
    {
        foreach (var fileInfo in source.GetFiles()) {
            var destPath = Path.Combine(target.FullName, fileInfo.Name);
            if (fileInfo.LinkTarget != null) {
                CreateSymlink(destPath, fileInfo.LinkTarget, isDirectory: false);
            } else {
                fileInfo.CopyTo(destPath, true);
                File.SetLastWriteTimeUtc(destPath, fileInfo.LastWriteTimeUtc);
                File.SetLastAccessTimeUtc(destPath, fileInfo.LastAccessTimeUtc);
                File.SetCreationTimeUtc(destPath, fileInfo.CreationTimeUtc);
            }
        }

        foreach (var sourceSubDir in source.GetDirectories()) {
            if (sourceSubDir.LinkTarget != null) {
                CreateSymlink(Path.Combine(target.FullName, sourceSubDir.Name), sourceSubDir.LinkTarget, isDirectory: true);
            } else {
                var targetSubDir = target.CreateSubdirectory(sourceSubDir.Name);
                CopyFilesRecursive(sourceSubDir, targetSubDir);
                Directory.SetLastWriteTimeUtc(targetSubDir.FullName, sourceSubDir.LastWriteTimeUtc);
                Directory.SetLastAccessTimeUtc(targetSubDir.FullName, sourceSubDir.LastAccessTimeUtc);
                Directory.SetCreationTimeUtc(targetSubDir.FullName, sourceSubDir.CreationTimeUtc);
            }
        }
    }

    private static void CreateSymlink(string path, string target, bool isDirectory)
    {
        try {
            if (isDirectory) {
                Directory.CreateSymbolicLink(path, target);
            } else {
                File.CreateSymbolicLink(path, target);
            }
        } catch (UnauthorizedAccessException ex) {
            if (VelopackRuntimeInfo.IsWindows) {
                throw new UserInfoException(
                    $"Failed to create symlink '{path}': access denied. " +
                    "On Windows, creating symlinks requires Developer Mode to be enabled " +
                    "(Settings > For developers > Developer Mode) or the 'Create symbolic links' group policy.",
                    ex);
            }

            throw new UserInfoException($"Failed to create symlink '{path}': access denied.", ex);
        }
    }
}
