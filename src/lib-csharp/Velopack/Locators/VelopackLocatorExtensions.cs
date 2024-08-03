using System.IO;

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
            return Path.Combine(locator.PackagesDir!, Utility.GetSafeFilename(velopackAsset.FileName));
        }

    }
}