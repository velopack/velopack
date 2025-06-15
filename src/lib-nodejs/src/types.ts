// This file is auto-generated. Do not edit by hand.

/** VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth). */
export type VelopackLocatorConfig = {
    /** The root directory of the current app. */
    RootAppDir: string,
    /** The path to the Update.exe binary. */
    UpdateExePath: string,
    /** The path to the packages' directory. */
    PackagesDir: string,
    /** The current app manifest. */
    ManifestPath: string,
    /** The directory containing the application's user binaries. */
    CurrentBinaryDir: string,
    /** Whether the current application is portable or installed. */
    IsPortable: boolean,
}

/** An individual Velopack asset, could refer to an asset on-disk or in a remote package feed. */
export type VelopackAsset = {
    /** The name or Id of the package containing this release. */
    PackageId: string,
    /** The version of this release. */
    Version: string,
    /** The type of asset (eg. "Full" or "Delta"). */
    Type: string,
    /** The filename of the update package containing this release. */
    FileName: string,
    /** The SHA1 checksum of the update package containing this release. */
    SHA1: string,
    /** The SHA256 checksum of the update package containing this release. */
    SHA256: string,
    /** The size in bytes of the update package containing this release. */
    Size: number,
    /** The release notes in markdown format, as passed to Velopack when packaging the release. This may be an empty string. */
    NotesMarkdown: string,
    /** The release notes in HTML format, transformed from Markdown when packaging the release. This may be an empty string. */
    NotesHtml: string,
}

/** Holds information about the current version and pending updates, such as how many there are, and access to release notes. */
export type UpdateInfo = {
    /** The available version that we are updating to. */
    TargetFullRelease: VelopackAsset,
    /** The base release that this update is based on. This is only available if the update is a delta update. */
    BaseRelease?: VelopackAsset,
    /** The list of delta updates that can be applied to the base version to get to the target version. */
    DeltasToTarget: VelopackAsset[],
    /**
     * True if the update is a version downgrade or lateral move (such as when switching channels to the same version number).
     * In this case, only full updates are allowed, and any local packages on disk newer than the downloaded version will be
     * deleted.
     */
    IsDowngrade: boolean,
}

/** Options to customise the behaviour of UpdateManager. */
export type UpdateOptions = {
    /**
     * Allows UpdateManager to update to a version that's lower than the current version (i.e. downgrading).
     * This could happen if a release has bugs and was retracted from the release feed, or if you're using
     * ExplicitChannel to switch channels to another channel where the latest version on that
     * channel is lower than the current version.
     */
    AllowVersionDowngrade: boolean,
    /**
     * **This option should usually be left None**.
     * Overrides the default channel used to fetch updates.
     * The default channel will be whatever channel was specified on the command line when building this release.
     * For example, if the current release was packaged with '--channel beta', then the default channel will be 'beta'.
     * This allows users to automatically receive updates from the same channel they installed from. This options
     * allows you to explicitly switch channels, for example if the user wished to switch back to the 'stable' channel
     * without having to reinstall the application.
     */
    ExplicitChannel?: string,
    /**
     * Sets the maximum number of deltas to consider before falling back to a full update.
     * The default is 10. Set to a negative number (eg. -1) to disable deltas.
     */
    MaximumDeltasBeforeFallback: number,
}

