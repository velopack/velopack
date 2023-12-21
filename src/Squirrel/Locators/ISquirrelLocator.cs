﻿using System;
using System.Collections.Generic;
using NuGet.Versioning;

namespace Squirrel.Locators
{
    /// <summary>
    /// An interface describing where Squirrel can find key folders and files.
    /// </summary>
    public interface ISquirrelLocator
    {
        /// <summary> The unique application Id. This is used in various app paths. </summary>
        public string AppId { get; }

        /// <summary> 
        /// The root directory of the application. On Windows, this folder contains all 
        /// the application files, but that may not be the case on other operating systems. 
        /// </summary>
        public string RootAppDir { get; }

        /// <summary> The directory in which nupkg files are stored for this application. </summary>
        public string PackagesDir { get; }

        /// <summary> The directory in which versioned application files are stored. </summary>
        public string AppContentDir { get; }

        /// <summary> The temporary directory for this application. </summary>
        public string AppTempDir { get; }

        /// <summary> The path to the current Update.exe or similar on other operating systems. </summary>
        public string UpdateExePath { get; }

        /// <summary> The currently installed version of the application, or null if the app is not installed. </summary>
        public SemanticVersion CurrentlyInstalledVersion { get; }

        /// <summary>
        /// Finds .nupkg files in the PackagesDir and returns a list of ReleaseEntryName objects.
        /// </summary>
        public List<ReleaseEntry> GetLocalPackages();

        /// <summary>
        /// Finds latest .nupkg file in the PackagesDir or null if not found.
        /// </summary>
        public ReleaseEntry GetLatestLocalPackage();

        /// <summary>
        /// Unique identifier for this user which is used to calculate whether this user is eligible for 
        /// staged roll outs.
        /// </summary>
        public Guid? GetOrCreateStagedUserId();
    }
}