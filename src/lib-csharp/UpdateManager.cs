using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using Velopack.Compression;
using Velopack.Exceptions;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.NuGet;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack
{
    /// <summary>
    /// Provides functionality for checking for updates, downloading updates, and applying updates to the current application.
    /// </summary>
    public partial class UpdateManager
    {
        /// <summary> The currently installed application Id. This would be what you set when you create your release.</summary>
        public virtual string? AppId => Locator.AppId;

        /// <summary> True if this application is currently installed, and is able to download/check for updates. </summary>
        public virtual bool IsInstalled => Locator.CurrentlyInstalledVersion != null;

        /// <inheritdoc cref="IVelopackLocator.IsPortable" />
        public virtual bool IsPortable => Locator.IsPortable;

        /// <summary> OBSOLETE: Use <see cref="UpdatePendingRestart"/> instead. </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use UpdatePendingRestart instead.")]
        public virtual bool IsUpdatePendingRestart => UpdatePendingRestart != null;

        /// <summary> Returns a VelopackAsset if there is a local update prepared that requires a call to <see cref="ApplyUpdatesAndRestart(VelopackAsset, string[])"/> to be applied. </summary>
        public virtual VelopackAsset? UpdatePendingRestart {
            get {
                var latestLocal = Locator.GetLatestLocalFullPackage();
                if (latestLocal != null && CurrentVersion != null && latestLocal.Version > CurrentVersion)
                    return latestLocal;
                return null;
            }
        }

        /// <summary> The currently installed app version when you created your release. Null if this is not a currently installed app. </summary>
        public virtual SemanticVersion? CurrentVersion => Locator.CurrentlyInstalledVersion;

        /// <summary> The update source to use when checking for/downloading updates. </summary>
        protected IUpdateSource Source { get; }

        /// <summary> The logger to use for diagnostic messages. </summary>
        protected IVelopackLogger Log { get; }

        /// <summary> The locator to use when searching for local file paths. </summary>
        protected IVelopackLocator Locator { get; }

        /// <summary> The channel to use when searching for packages. </summary>
        protected string Channel { get; }

        /// <summary> The default channel to search for packages in, if one was not provided by the user. </summary>
        protected string DefaultChannel => Locator?.Channel ?? VelopackRuntimeInfo.SystemOs.GetOsShortName();

        /// <summary> If true, an explicit channel was provided by the user, and it's different than the default channel. </summary>
        protected bool IsNonDefaultChannel => Locator?.Channel != null && Channel != DefaultChannel;

        /// <summary> If true, UpdateManager should return the latest asset in the feed, even if that version is lower than the current version. </summary>
        protected bool ShouldAllowVersionDowngrade { get; }

        /// <summary>
        /// Creates a new UpdateManager instance using the specified URL or file path to the releases feed, and the specified channel name.
        /// </summary>
        /// <param name="urlOrPath">A basic URL or file path to use when checking for updates.</param>
        /// <param name="options">Override / configure default update behaviors.</param>
        /// <param name="locator">This should usually be left null. Providing an <see cref="IVelopackLocator" /> allows you to mock up certain application paths. 
        /// For example, if you wanted to test that updates are working in a unit test, you could provide an instance of <see cref="TestVelopackLocator"/>. </param>
        public UpdateManager(string urlOrPath, UpdateOptions? options = null, IVelopackLocator? locator = null)
            : this(CreateSimpleSource(urlOrPath), options, locator)
        {
        }

        /// <summary>
        /// Creates a new UpdateManager instance using the specified URL or file path to the releases feed, and the specified channel name.
        /// </summary>
        /// <param name="source">The source describing where to search for updates. This can be a custom source, if you are integrating with some private resource,
        /// or it could be one of the predefined sources. (eg. <see cref="SimpleWebSource"/> or <see cref="GithubSource"/>, etc).</param>
        /// <param name="options">Override / configure default update behaviors.</param>
        /// <param name="locator">This should usually be left null. Providing an <see cref="IVelopackLocator" /> allows you to mock up certain application paths. 
        /// For example, if you wanted to test that updates are working in a unit test, you could provide an instance of <see cref="TestVelopackLocator"/>. </param>
        public UpdateManager(IUpdateSource source, UpdateOptions? options = null, IVelopackLocator? locator = null)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Locator = locator ?? VelopackLocator.Current;
            Log = Locator.Log;
            Channel = options?.ExplicitChannel ?? DefaultChannel;
            ShouldAllowVersionDowngrade = options?.AllowVersionDowngrade ?? false;
        }

        /// <inheritdoc cref="CheckForUpdatesAsync()"/>
        public UpdateInfo? CheckForUpdates()
        {
            return CheckForUpdatesAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Checks for updates, returning null if there are none available. If there are updates available, this method will return an 
        /// UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
        /// </summary>
        /// <returns>Null if no updates, otherwise <see cref="UpdateInfo"/> containing the version of the latest update available.</returns>
        public virtual async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            EnsureInstalled();
            var installedVer = CurrentVersion!;
            var stagedUserId = Locator.GetOrCreateStagedUserId();
            var latestLocalFull = Locator.GetLatestLocalFullPackage();

            Log.Debug("Retrieving latest release feed.");
            var feedObj = await Source.GetReleaseFeed(Log, AppId, Channel, stagedUserId, latestLocalFull).ConfigureAwait(false);
            var feed = feedObj.Assets;

            var latestRemoteFull = feed.Where(r => r.Type == VelopackAssetType.Full).MaxByPolyfill(x => x.Version).FirstOrDefault();
            if (latestRemoteFull == null) {
                Log.Info("No remote full releases found.");
                return null;
            }

            // there's a newer version available, easy.
            if (latestRemoteFull.Version > installedVer) {
                Log.Info($"Found newer remote release available ({installedVer} -> {latestRemoteFull.Version}).");
                return CreateDeltaUpdateStrategy(feed, latestLocalFull, latestRemoteFull);
            }

            // if the remote version is < than current version and downgrade is enabled
            if (latestRemoteFull.Version < installedVer && ShouldAllowVersionDowngrade) {
                Log.Info($"Latest remote release is older than current, and downgrade is enabled ({installedVer} -> {latestRemoteFull.Version}).");
                return new UpdateInfo(latestRemoteFull, true);
            }

            // if the remote version is the same as current version, and downgrade is enabled,
            // and we're searching for a different channel than current
            if (ShouldAllowVersionDowngrade && IsNonDefaultChannel) {
                if (VersionComparer.Compare(latestRemoteFull.Version, installedVer, VersionComparison.Version) == 0) {
                    Log.Info(
                        $"Latest remote release is the same version of a different channel, and downgrade is enabled ({installedVer}: {DefaultChannel} -> {Channel}).");
                    return new UpdateInfo(latestRemoteFull, true);
                }
            }

            Log.Info(
                $"No updates, remote version ({latestRemoteFull.Version}) is not newer than current version ({installedVer}) and / or downgrade is not enabled.");
            return null;
        }

        /// <summary>
        /// Given a feed of releases, and the latest local full release, and the latest remote full release, this method will return a delta
        /// update strategy to be used by <see cref="DownloadUpdatesAsync(UpdateInfo, Action{int}?, bool, CancellationToken)"/>.
        /// </summary>
        protected virtual UpdateInfo CreateDeltaUpdateStrategy(VelopackAsset[] feed, VelopackAsset? latestLocalFull, VelopackAsset latestRemoteFull)
        {
            if (latestLocalFull == null) {
                // TODO: for now, we're not trying to handle the case of building delta updates on top of an installation directory,
                // but we can look at this in the future. Until then, Windows (installer) is the only thing which ships with a complete .nupkg
                // so in all other cases, Velopack needs to download one full release before it can start using delta's.
                Log.Info("There is no local/base package available for this update, so delta updates will be disabled.");
                return new UpdateInfo(latestRemoteFull, false);
            }

            EnsureInstalled();
            var installedVer = CurrentVersion!;

            var matchingRemoteDelta = feed.Where(r => r.Type == VelopackAssetType.Delta && r.Version == latestRemoteFull.Version).FirstOrDefault();
            if (matchingRemoteDelta == null) {
                Log.Info($"Unable to find any delta matching version {latestRemoteFull.Version}, so delta updates will be disabled.");
                return new UpdateInfo(latestRemoteFull, false);
            }

            // if we have a local full release, we try to apply delta's from that version to target version.
            SemanticVersion deltaFromVer = latestLocalFull.Version;

            var deltas = feed.Where(r => r.Type == VelopackAssetType.Delta && r.Version > deltaFromVer && r.Version <= latestRemoteFull.Version).ToArray();
            Log.Debug($"Found {deltas.Length} delta release(s) between {deltaFromVer} and {latestRemoteFull.Version}.");
            return new UpdateInfo(latestRemoteFull, false, latestLocalFull, deltas);
        }

        /// <inheritdoc cref="DownloadUpdatesAsync(UpdateInfo, Action{int}, bool, CancellationToken)"/>
        public void DownloadUpdates(UpdateInfo updates, Action<int>? progress = null, bool ignoreDeltas = false)
        {
            DownloadUpdatesAsync(updates, progress, ignoreDeltas)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Downloads the specified updates to the local app packages directory. If the update contains delta packages and ignoreDeltas=false, 
        /// this method will attempt to unpack and prepare them. If there is no delta update available, or there is an error preparing delta 
        /// packages, this method will fall back to downloading the full version of the update. This function will acquire a global update lock
        /// so may fail if there is already another update operation in progress.
        /// </summary>
        /// <param name="updates">The updates to download. Should be retrieved from <see cref="CheckForUpdates"/>.</param>
        /// <param name="progress">The progress callback. Will be called with values from 0-100.</param>
        /// <param name="ignoreDeltas">Whether to attempt downloading delta's or skip to full package download.</param>
        /// <param name="cancelToken">An optional cancellation token if you wish to stop this operation.</param>
        public virtual async Task DownloadUpdatesAsync(
            UpdateInfo updates, Action<int>? progress = null, bool ignoreDeltas = false, CancellationToken cancelToken = default)
        {
            progress ??= (_ => { });

            // the progress delegate may very likely invoke into the client main thread for UI updates, so
            // let's try to reduce the spam. report only on even numbers and only if the progress has changed.
            int lastProgress = 0;

            void reportProgress(int x)
            {
                int result = (int) (Math.Round(x / 2d, MidpointRounding.AwayFromZero) * 2d);
                if (result != lastProgress) {
                    lastProgress = result;
                    progress(result);
                }
            }

            if (updates == null) {
                throw new ArgumentNullException(nameof(updates));
            }

            var targetRelease = updates.TargetFullRelease;
            if (targetRelease == null) {
                throw new ArgumentException("Must pass a valid UpdateInfo object with a non-null TargetFullRelease", nameof(updates));
            }

            EnsureInstalled();
            using var _mut = await AcquireUpdateLock().ConfigureAwait(false);

            var appTempDir = Locator.AppTempDir!;

            var completeFile = Locator.GetLocalPackagePath(targetRelease);
            var incompleteFile = completeFile + ".partial";

            try {
                // if the package already exists on disk, we can skip the download.
                if (File.Exists(completeFile)) {
                    Log.Info($"Package already exists on disk: '{completeFile}', verifying checksum...");
                    try {
                        VerifyPackageChecksum(targetRelease, completeFile);
                        Log.Info("Package checksum verified, skipping download.");
                        return;
                    } catch (ChecksumFailedException ex) {
                        Log.Warn(ex, $"Checksum failed for file '{completeFile}'. Deleting and starting over.");
                    }
                }

                var deltasSize = updates.DeltasToTarget.Sum(x => x.Size);
                var deltasCount = updates.DeltasToTarget.Count();

                try {
                    if (updates.BaseRelease?.FileName != null && deltasCount > 0) {
                        if (ignoreDeltas) {
                            Log.Info("Ignoring delta updates (ignoreDeltas parameter)");
                        } else {
                            if (deltasCount > 10 || deltasSize > targetRelease.Size) {
                                Log.Info(
                                    $"There are too many delta's ({deltasCount} > 10) or the sum of their size ({deltasSize} > {targetRelease.Size}) is too large. " +
                                    $"Only full update will be available.");
                            } else {
                                using var _1 = TempUtil.GetTempDirectory(out var deltaStagingDir, appTempDir);
                                string basePackagePath = Locator.GetLocalPackagePath(updates.BaseRelease);
                                if (!File.Exists(basePackagePath))
                                    throw new Exception($"Unable to find base package {basePackagePath} for delta update.");
                                EasyZip.ExtractZipToDirectory(Log, basePackagePath, deltaStagingDir);

                                reportProgress(10);
                                await DownloadAndApplyDeltaUpdates(
                                        deltaStagingDir,
                                        updates,
                                        x => reportProgress(CoreUtil.CalculateProgress(x, 10, 80)),
                                        cancelToken)
                                    .ConfigureAwait(false);
                                reportProgress(80);

                                Log.Info("Delta updates completed, creating final update package.");
                                File.Delete(incompleteFile);
                                await EasyZip.CreateZipFromDirectoryAsync(
                                    Log,
                                    incompleteFile,
                                    deltaStagingDir,
                                    x => reportProgress(CoreUtil.CalculateProgress(x, 80, 100)),
                                    cancelToken: cancelToken).ConfigureAwait(false);
                                File.Delete(completeFile);
                                File.Move(incompleteFile, completeFile);
                                Log.Info("Delta release preparations complete. Package moved to: " + completeFile);
                                reportProgress(100);
                                return; // success!
                            }
                        }
                    }
                } catch (Exception ex) when (!VelopackRuntimeInfo.InUnitTestRunner) {
                    Log.Warn(ex, "Unable to apply delta updates, falling back to full update.");
                }

                Log.Info($"Downloading full release ({targetRelease.FileName})");
                File.Delete(incompleteFile);
                await Source.DownloadReleaseEntry(Log, targetRelease, incompleteFile, reportProgress, cancelToken).ConfigureAwait(false);
                Log.Info("Verifying package checksum...");
                VerifyPackageChecksum(targetRelease, incompleteFile);

                IoUtil.MoveFile(incompleteFile, completeFile, true);
                Log.Info("Full release download complete. Package moved to: " + completeFile);
                reportProgress(100);
            } finally {
                if (VelopackRuntimeInfo.IsWindows && !cancelToken.IsCancellationRequested) {
                    try {
                        var updateExe = Locator.UpdateExePath!;
                        Log.Info("Extracting new Update.exe to " + updateExe);
                        var zip = new ZipPackage(completeFile, loadUpdateExe: true);

                        if (zip.UpdateExeBytes == null) {
                            Log.Error("Update.exe not found in package, skipping extraction.");
                        } else {
                            await IoUtil.RetryAsync(
                                async () => {
                                    using var ms = new MemoryStream(zip.UpdateExeBytes);
                                    using var fs = File.Create(updateExe);
                                    await ms.CopyToAsync(fs).ConfigureAwait(false);
                                }).ConfigureAwait(false);
                        }
                    } catch (Exception ex) {
                        Log.Error(ex, "Failed to extract new Update.exe");
                    }
                }

                CleanPackagesExcept(completeFile);
            }
        }

        /// <summary>
        /// Given a folder containing the extracted base package, and a list of delta updates, downloads and applies the 
        /// delta updates to the base package.
        /// </summary>
        /// <param name="extractedBasePackage">A folder containing the application files to apply the delta's to.</param>
        /// <param name="updates">An update object containing one or more delta's</param>
        /// <param name="progress">A callback reporting process of delta application progress (from 0-100).</param>
        /// <param name="cancelToken">A token to use to cancel the request.</param>
        protected virtual async Task DownloadAndApplyDeltaUpdates(string extractedBasePackage, UpdateInfo updates, Action<int> progress,
            CancellationToken cancelToken)
        {
            var releasesToDownload = updates.DeltasToTarget.OrderBy(d => d.Version).ToArray();

            var appTempDir = Locator.AppTempDir!;
            var updateExe = Locator.UpdateExePath!;

            // downloading accounts for 0%-50% of progress
            double current = 0;
            double toIncrement = 100.0 / releasesToDownload.Count();
            await releasesToDownload.ForEachAsync(
                async x => {
                    var targetFile = Locator.GetLocalPackagePath(x);
                    double component = 0;
                    Log.Debug($"Downloading delta version {x.Version}");
                    await Source.DownloadReleaseEntry(
                        Log,
                        x,
                        targetFile,
                        p => {
                            lock (progress) {
                                current -= component;
                                component = toIncrement / 100.0 * p;
                                var progressOfStep = (int) Math.Round(current += component);
                                progress(CoreUtil.CalculateProgress(progressOfStep, 0, 50));
                            }
                        },
                        cancelToken).ConfigureAwait(false);
                    VerifyPackageChecksum(x, targetFile);
                    cancelToken.ThrowIfCancellationRequested();
                    Log.Debug($"Download complete for delta version {x.Version}");
                }).ConfigureAwait(false);

            Log.Info("All delta packages downloaded and verified, applying them to the base now. The delta staging dir is: " + extractedBasePackage);

            // applying deltas accounts for 50%-100% of progress
            double progressStepSize = 100d / releasesToDownload.Length;
            var builder = new DeltaUpdateExe(Log, appTempDir, updateExe);
            for (var i = 0; i < releasesToDownload.Length; i++) {
                cancelToken.ThrowIfCancellationRequested();
                var rel = releasesToDownload[i];
                double baseProgress = i * progressStepSize;
                var packageFile = Locator.GetLocalPackagePath(rel);
                builder.ApplyDeltaPackageFast(
                    extractedBasePackage,
                    packageFile,
                    x => {
                        var progressOfStep = (int) (baseProgress + (progressStepSize * (x / 100d)));
                        progress(CoreUtil.CalculateProgress(progressOfStep, 50, 100));
                    });
            }

            progress(100);
        }

        /// <summary>
        /// Removes any incomplete files (.partial) and packages (.nupkg) from the packages directory that does not match
        /// the provided asset. If assetToKeep is null, all packages will be deleted.
        /// </summary>
        protected virtual void CleanPackagesExcept(string? assetToKeep)
        {
            try {
                Log.Info("Cleaning up incomplete and delta packages from packages directory.");

                var appPackageDir = Locator.PackagesDir!;
                foreach (var l in Directory.EnumerateFiles(appPackageDir, "*.nupkg").ToArray()) {
                    try {
                        if (assetToKeep != null && PathUtil.FullPathEquals(l, assetToKeep)) {
                            continue;
                        }

                        IoUtil.DeleteFileOrDirectoryHard(l);
                        Log.Trace(l + " deleted.");
                    } catch (Exception ex) {
                        Log.Warn(ex, "Failed to delete partial package: " + l);
                    }
                }

                foreach (var l in Directory.EnumerateFiles(appPackageDir, "*.partial").ToArray()) {
                    try {
                        IoUtil.DeleteFileOrDirectoryHard(l);
                        Log.Trace(l + " deleted.");
                    } catch (Exception ex) {
                        Log.Warn(ex, "Failed to delete partial package: " + l);
                    }
                }
            } catch (Exception ex) {
                Log.Warn(ex, "Failed to clean up incomplete and delta packages.");
            }
        }

        /// <summary>
        /// Check a package checksum against the one in the release entry, and throws if the checksum does not match.
        /// </summary>
        /// <param name="release">The entry to check</param>
        /// <param name="filePathOverride">Optional file path, if not specified the package will be loaded from %pkgdir%/release.OriginalFilename.</param>
        protected internal virtual void VerifyPackageChecksum(VelopackAsset release, string? filePathOverride = null)
        {
            var targetPackage = new FileInfo(filePathOverride ?? Locator.GetLocalPackagePath(release));

            if (!targetPackage.Exists) {
                throw new ChecksumFailedException(targetPackage.FullName, "File doesn't exist.");
            }

            if (targetPackage.Length != release.Size) {
                throw new ChecksumFailedException(targetPackage.FullName, $"Size doesn't match ({targetPackage.Length} != {release.Size}).");
            }

            if (!string.IsNullOrEmpty(release.SHA256)) {
                var hash = IoUtil.CalculateFileSHA256(targetPackage.FullName);
                if (!hash.Equals(release.SHA256, StringComparison.Ordinal)) {
                    throw new ChecksumFailedException(targetPackage.FullName, $"SHA256 doesn't match ({release.SHA256} != {hash}).");
                }
            } else {
                var hash = IoUtil.CalculateFileSHA1(targetPackage.FullName);
                if (!hash.Equals(release.SHA1, StringComparison.OrdinalIgnoreCase)) {
                    throw new ChecksumFailedException(targetPackage.FullName, $"SHA1 doesn't match ({release.SHA1} != {hash}).");
                }
            }
        }

        /// <summary>
        /// Throws an exception if the current application is not installed.
        /// </summary>
        protected virtual void EnsureInstalled()
        {
            if (AppId == null || !IsInstalled)
                throw new NotInstalledException();
        }

        /// <summary>
        /// Acquires a globally unique mutex/lock for the current application, to avoid concurrent install/uninstall/update operations.
        /// </summary>
        protected virtual async Task<IDisposable> AcquireUpdateLock()
        {
            var dir = Directory.CreateDirectory(Locator.PackagesDir!);
            var lockPath = Path.Combine(dir.FullName, ".velopack_lock");
            var fsLock = new LockFile(lockPath);
            await fsLock.LockAsync().ConfigureAwait(false);
            return fsLock;
        }

        private static IUpdateSource CreateSimpleSource(string urlOrPath)
        {
            if (String.IsNullOrWhiteSpace(urlOrPath)) {
                throw new ArgumentException("Must pass a valid URL or file path to UpdateManager", nameof(urlOrPath));
            }

            if (HttpUtil.IsHttpUrl(urlOrPath)) {
                return new SimpleWebSource(urlOrPath, HttpUtil.CreateDefaultDownloader());
            } else {
                return new SimpleFileSource(new DirectoryInfo(urlOrPath));
            }
        }
    }
}