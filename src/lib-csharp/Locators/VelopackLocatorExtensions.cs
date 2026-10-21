using System.IO;
using Velopack.Util;

namespace Velopack.Locators
{
    /// <summary>
    /// An interface describing where Velopack can find key folders and files.
    /// </summary>
    internal static class VelopackLocatorExtensions
    {
        /// <summary>
        /// Get the file path that should be used for storing <paramref name="velopackAsset"/> in the local 
        /// <see cref="IVelopackLocator.PackagesDir"/>
        /// </summary>
        public static string GetLocalPackagePath(this IVelopackLocator locator, VelopackAsset velopackAsset)
        {
            return Path.Combine(locator.PackagesDir!, PathUtil.GetSafeFilename(velopackAsset.FileName));
        }

        /// <summary>
        /// The directory holding staged chunk updates (<c>{PackagesDir}/staged</c>), or null when there is no packages directory.
        /// </summary>
        public static string? GetStagedRootDir(this IVelopackLocator locator)
        {
            var packages = locator.PackagesDir;
            return packages == null ? null : Path.Combine(packages, "staged");
        }

        /// <summary>
        /// The staging directory of one chunk update (<c>{PackagesDir}/staged/{stagedId}</c>).
        /// </summary>
        public static string GetStagedDir(this IVelopackLocator locator, string stagedId)
        {
            return Path.Combine(locator.PackagesDir!, "staged", stagedId);
        }
    }
}