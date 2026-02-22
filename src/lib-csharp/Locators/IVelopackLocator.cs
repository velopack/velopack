using System;
using System.Collections.Generic;
using NuGet.Versioning;
using Velopack.Logging;

namespace Velopack.Locators
{
    /// <summary>
    /// An interface describing where Velopack can find key folders and files.
    /// </summary>
    public interface IVelopackLocator
    {
        /// <summary> The unique application Id. This is used in various app paths. </summary>
        string? AppId { get; }

        /// <summary> 
        /// The root directory of the application. On Windows, this folder contains all 
        /// the application files, but that may not be the case on other operating systems. 
        /// </summary>
        string? RootAppDir { get; }

        /// <summary> The directory in which nupkg files are stored for this application. </summary>
        string? PackagesDir { get; }

        /// <summary> The directory in which versioned application files are stored. </summary>
        string? AppContentDir { get; }

        /// <summary> The temporary directory for this application. </summary>
        string? AppTempDir { get; }

        /// <summary> The path to the current Update.exe or similar on other operating systems. </summary>
        string? UpdateExePath { get; }

        /// <summary> The currently installed version of the application, or null if the app is not installed. </summary>
        SemanticVersion? CurrentlyInstalledVersion { get; }

        /// <summary> The path from <see cref="AppContentDir"/> to this executable. </summary>
        string? ThisExeRelativePath { get; }

        /// <summary> The release channel this package was built for. </summary>
        string? Channel { get; }
        
        /// <summary> The logging interface to use for Velopack diagnostic messages. </summary>
        IVelopackLogger Log { get; }
        
        /// <summary>
        /// A flag indicating if this is a portable build, and that the settings should be self-contained in the package.
        /// On Windows, this is true for portable builds, and false for non-portable builds which were installed by Setup.exe
        /// On OSX and Linux, this is always false, because settings and application files should be stored in the user's 
        /// home directory.
        /// </summary>
        bool IsPortable { get; }
        
        /// <summary>
        /// Provides an abstraction for dealing with the dotnet Process API. This is used to start processes and
        /// get information about the current process.
        /// </summary>
        IProcessImpl Process { get; }
        
        /// <summary>
        /// Add a logger to the list of loggers. This will be used to log messages from Velopack.
        /// </summary>
        /// <param name="logger"></param>
        void AddLogger(IVelopackLogger logger);
        
        /// <summary>
        /// Finds .nupkg files in the PackagesDir and returns a list of ReleaseEntryName objects.
        /// </summary>
        List<VelopackAsset> GetLocalPackages();

        /// <summary>
        /// Finds latest .nupkg file in the PackagesDir or null if not found.
        /// </summary>
        VelopackAsset? GetLatestLocalFullPackage();

        /// <summary>
        /// Unique identifier for this user which is used to calculate whether this user is eligible for 
        /// staged roll outs.
        /// </summary>
        Guid? GetOrCreateStagedUserId();
    }
}