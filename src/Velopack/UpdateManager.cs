using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using Velopack.Compression;
using Velopack.Locators;
using Velopack.NuGet;
using Velopack.Sources;

namespace Velopack
{
    /// <summary>
    /// Provides functionality for checking for updates, downloading updates, and applying updates to the current application.
    /// </summary>
    public class UpdateManager
    {
        /// <summary> The currently installed application Id. This would be what you set when you create your release.</summary>
        public virtual string? AppId => Locator.AppId;

        /// <summary> True if this application is currently installed, and is able to download/check for updates. </summary>
        public virtual bool IsInstalled => Locator.CurrentlyInstalledVersion != null;

        /// <summary> True if there is a local update prepared that requires a call to <see cref="ApplyUpdatesAndRestart(string[])"/> to be applied. </summary>
        public virtual bool IsUpdatePendingRestart {
            get {
                var latestLocal = Locator.GetLatestLocalFullPackage();
                if (latestLocal != null && CurrentVersion != null && latestLocal.Version > CurrentVersion)
                    return true;
                return false;
            }
        }

        /// <summary> The currently installed app version when you created your release. Null if this is not a currently installed app. </summary>
        public virtual SemanticVersion? CurrentVersion => Locator.CurrentlyInstalledVersion;

        /// <summary> The update source to use when checking for/downloading updates. </summary>
        protected IUpdateSource Source { get; }

        /// <summary> The logger to use for diagnostic messages. </summary>
        protected ILogger Log { get; }

        /// <summary> The locator to use when searching for local file paths. </summary>
        protected IVelopackLocator Locator { get; }

        /// <summary> The channel to use when searching for packages. </summary>
        protected string Channel { get; }

        /// <summary>
        /// Creates a new UpdateManager instance using the specified URL or file path to the releases feed, and the specified channel name.
        /// </summary>
        /// <param name="urlOrPath">A basic URL or file path to use when checking for updates.</param>
        /// <param name="channel">Search for releases in the feed of a specific channel name. If null, it will search the default channel.</param>
        /// <param name="logger">The logger to use for diagnostic messages. If one was provided to <see cref="VelopackApp.Run(ILogger)"/> but is null here, 
        /// it will be cached and used again.</param>
        /// <param name="locator">This should usually be left null. Providing an <see cref="IVelopackLocator" /> allows you to mock up certain application paths. 
        /// For example, if you wanted to test that updates are working in a unit test, you could provide an instance of <see cref="TestVelopackLocator"/>. </param>
        public UpdateManager(string urlOrPath, string? channel = null, ILogger? logger = null, IVelopackLocator? locator = null)
            : this(CreateSimpleSource(urlOrPath), channel, logger, locator)
        {
        }

        /// <summary>
        /// Creates a new UpdateManager instance using the specified URL or file path to the releases feed, and the specified channel name.
        /// </summary>
        /// <param name="source">The source describing where to search for updates. This can be a custom source, if you are integrating with some private resource,
        /// or it could be one of the predefined sources. (eg. <see cref="SimpleWebSource"/> or <see cref="GithubSource"/>, etc).</param>
        /// <param name="channel">Search for releases in the feed of a specific channel name. If null, it will search the default channel.</param>
        /// <param name="logger">The logger to use for diagnostic messages. If one was provided to <see cref="VelopackApp.Run(ILogger)"/> but is null here, 
        /// it will be cached and used again.</param>
        /// <param name="locator">This should usually be left null. Providing an <see cref="IVelopackLocator" /> allows you to mock up certain application paths. 
        /// For example, if you wanted to test that updates are working in a unit test, you could provide an instance of <see cref="TestVelopackLocator"/>. </param>
        public UpdateManager(IUpdateSource source, string? channel = null, ILogger? logger = null, IVelopackLocator? locator = null)
        {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }
            Source = source;
            Log = logger ?? VelopackApp.DefaultLogger ?? NullLogger.Instance;
            Locator = locator ?? VelopackLocator.GetDefault(Log);
            Channel = channel ?? Locator.Channel ?? VelopackRuntimeInfo.SystemOs.GetOsShortName();
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
            var betaId = Locator.GetOrCreateStagedUserId();
            var latestLocalFull = Locator.GetLatestLocalFullPackage();

            Log.Debug("Retrieving latest release feed.");
            var feedObj = await Source.GetReleaseFeed(Log, Channel, betaId, latestLocalFull).ConfigureAwait(false);
            var feed = feedObj.Assets;

            var latestRemoteFull = feed.Where(r => r.Type == VelopackAssetType.Full).MaxBy(x => x.Version).FirstOrDefault();
            if (latestRemoteFull == null) {
                Log.Info("No remote full releases found.");
                return null;
            }

            if (latestRemoteFull.Version <= installedVer) {
                Log.Info($"No updates, remote version ({latestRemoteFull.Version}) is not newer than current version ({installedVer}).");
                return null;
            }

            Log.Info($"Found remote update available ({latestRemoteFull.Version}).");

            var matchingRemoteDelta = feed.Where(r => r.Type == VelopackAssetType.Delta && r.Version == latestRemoteFull.Version).FirstOrDefault();
            if (matchingRemoteDelta == null) {
                Log.Info($"Unable to find delta matching version {latestRemoteFull.Version}, only full update will be available.");
                return new UpdateInfo(latestRemoteFull);
            }

            // if we have a local full release, we try to apply delta's from that version to target version.
            // if we do not have a local release, we try to apply delta's to a copy of the current installed app files.
            SemanticVersion? deltaFromVer = null;
            if (latestLocalFull != null) {
                if (latestLocalFull.Version != installedVer) {
                    Log.Warn($"The current running version is {installedVer}, however the latest available local full .nupkg " +
                        $"is {latestLocalFull.Version}. We will try to download and apply delta's from {latestLocalFull.Version}.");
                }
                deltaFromVer = latestLocalFull.Version;
            } else {
                Log.Warn("There is no local release .nupkg, we are going to attempt an in-place delta upgrade using application files.");
                deltaFromVer = installedVer;
            }

            var deltas = feed.Where(r => r.Type == VelopackAssetType.Delta && r.Version > deltaFromVer && r.Version <= latestRemoteFull.Version).ToArray();
            Log.Debug($"Found {deltas.Length} delta releases between {deltaFromVer} and {latestRemoteFull.Version}.");
            return new UpdateInfo(latestRemoteFull, latestLocalFull, deltas);
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
            using var _mut = AcquireUpdateLock();

            var appTempDir = Locator.AppTempDir!;
            var appPackageDir = Locator.PackagesDir!;

            var completeFile = Path.Combine(appPackageDir, targetRelease.FileName);
            var incompleteFile = completeFile + ".partial";

            try {
                if (File.Exists(completeFile)) {
                    Log.Info($"Package already exists on disk: '{completeFile}', verifying checksum...");
                    try {
                        VerifyPackageChecksum(targetRelease);
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
                                Log.Info($"There are too many delta's ({deltasCount} > 10) or the sum of their size ({deltasSize} > {targetRelease.Size}) is too large. " +
                                    $"Only full update will be available.");
                            } else {
                                using var _1 = Utility.GetTempDirectory(out var deltaStagingDir, appTempDir);
                                string basePackagePath = Path.Combine(appPackageDir, updates.BaseRelease.FileName);
                                if (!File.Exists(basePackagePath))
                                    throw new Exception($"Unable to find base package {basePackagePath} for delta update.");
                                EasyZip.ExtractZipToDirectory(Log, basePackagePath, deltaStagingDir);

                                reportProgress(10);
                                await DownloadAndApplyDeltaUpdates(deltaStagingDir, updates, x => reportProgress(Utility.CalculateProgress(x, 10, 80)), cancelToken)
                                    .ConfigureAwait(false);
                                reportProgress(80);

                                Log.Info("Delta updates completed, creating final update package.");
                                File.Delete(incompleteFile);
                                await EasyZip.CreateZipFromDirectoryAsync(Log, incompleteFile, deltaStagingDir, x => reportProgress(Utility.CalculateProgress(x, 80, 100)),
                                    cancelToken: cancelToken).ConfigureAwait(false);
                                File.Delete(completeFile);
                                File.Move(incompleteFile, completeFile);
                                Log.Info("Delta release preparations complete. Package moved to: " + completeFile);
                                reportProgress(100);
                                return; // success!
                            }
                        }
                    }
                } catch (Exception ex) {
                    Log.Warn(ex, "Unable to apply delta updates, falling back to full update.");
                    if (VelopackRuntimeInfo.InUnitTestRunner) {
                        throw;
                    }
                }

                Log.Info($"Downloading full release ({targetRelease.FileName})");
                File.Delete(incompleteFile);
                await Source.DownloadReleaseEntry(Log, targetRelease, incompleteFile, reportProgress, cancelToken).ConfigureAwait(false);
                Log.Info("Verifying package checksum...");
                VerifyPackageChecksum(targetRelease, incompleteFile);
                File.Delete(completeFile);
                File.Move(incompleteFile, completeFile);
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
                            await Utility.RetryAsync(async () => {
                                using var ms = new MemoryStream(zip.UpdateExeBytes);
                                using var fs = File.Create(updateExe);
                                await ms.CopyToAsync(fs).ConfigureAwait(false);
                            }).ConfigureAwait(false);
                        }
                    } catch (Exception ex) {
                        Log.Error(ex, "Failed to extract new Update.exe");
                    }
                }

                CleanIncompleteAndDeltaPackages();
            }
        }

        /// <summary>
        /// This will exit your app immediately, apply updates, and then optionally relaunch the app using the specified 
        /// restart arguments. If you need to save state or clean up, you should do that before calling this method. 
        /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
        /// You can check if there are pending updates by checking <see cref="IsUpdatePendingRestart"/>.
        /// </summary>
        /// <param name="restartArgs">The arguments to pass to the application when it is restarted.</param>
        public void ApplyUpdatesAndRestart(string[]? restartArgs = null)
        {
            WaitExitThenApplyUpdates(true, restartArgs);
            Environment.Exit(0);
        }

        /// <summary>
        /// This will exit your app immediately, apply updates, and then optionally relaunch the app using the specified 
        /// restart arguments. If you need to save state or clean up, you should do that before calling this method. 
        /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
        /// You can check if there are pending updates by checking <see cref="IsUpdatePendingRestart"/>.
        /// </summary>
        public void ApplyUpdatesAndExit()
        {
            WaitExitThenApplyUpdates(false, null);
            Environment.Exit(0);
        }

        /// <summary>
        /// This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
        /// You should then clean up any state and exit your app. The updater will apply updates and then
        /// optionally restart your app. The updater will only wait for 60 seconds before giving up.
        /// You can check if there are pending updates by checking <see cref="IsUpdatePendingRestart"/>.
        /// </summary>
        /// <param name="restart">Whether Velopack should restart the app after the updates have been applied.</param>
        /// <param name="restartArgs">The arguments to pass to the application when it is restarted.</param>
        public void WaitExitThenApplyUpdates(bool restart = true, string[]? restartArgs = null)
        {
            UpdateExe.Apply(Locator, false, restart, restartArgs, Log);
        }

        /// <summary>
        /// Given a folder containing the extracted base package, and a list of delta updates, downloads and applies the 
        /// delta updates to the base package.
        /// </summary>
        /// <param name="extractedBasePackage">A folder containing the application files to apply the delta's to.</param>
        /// <param name="updates">An update object containing one or more delta's</param>
        /// <param name="progress">A callback reporting process of delta application progress (from 0-100).</param>
        /// <param name="cancelToken">A token to use to cancel the request.</param>
        protected virtual async Task DownloadAndApplyDeltaUpdates(string extractedBasePackage, UpdateInfo updates, Action<int> progress, CancellationToken cancelToken)
        {
            var releasesToDownload = updates.DeltasToTarget.OrderBy(d => d.Version).ToArray();

            var appTempDir = Locator.AppTempDir!;
            var appPackageDir = Locator.PackagesDir!;
            var updateExe = Locator.UpdateExePath!;

            // downloading accounts for 0%-50% of progress
            double current = 0;
            double toIncrement = 100.0 / releasesToDownload.Count();
            await releasesToDownload.ForEachAsync(async x => {
                var targetFile = Path.Combine(appPackageDir, x.FileName);
                double component = 0;
                Log.Debug($"Downloading delta version {x.Version}");
                await Source.DownloadReleaseEntry(Log, x, targetFile, p => {
                    lock (progress) {
                        current -= component;
                        component = toIncrement / 100.0 * p;
                        var progressOfStep = (int) Math.Round(current += component);
                        progress(Utility.CalculateProgress(progressOfStep, 0, 50));
                    }
                }, cancelToken).ConfigureAwait(false);
                VerifyPackageChecksum(x);
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
                var packageFile = Path.Combine(appPackageDir, rel.FileName);
                builder.ApplyDeltaPackageFast(extractedBasePackage, packageFile, x => {
                    var progressOfStep = (int) (baseProgress + (progressStepSize * (x / 100d)));
                    progress(Utility.CalculateProgress(progressOfStep, 50, 100));
                });
            }

            progress(100);
        }

        /// <summary>
        /// Removes any incomplete files (.partial) and delta packages (-delta.nupkg) from the packages directory.
        /// </summary>
        protected void CleanIncompleteAndDeltaPackages()
        {
            try {
                var appPackageDir = Locator.PackagesDir!;
                Log.Info("Cleaning up incomplete and delta packages from packages directory.");
                foreach (var l in Locator.GetLocalPackages()) {
                    if (l.Type == VelopackAssetType.Delta) {
                        try {
                            var pkgPath = Path.Combine(appPackageDir, l.FileName);
                            File.Delete(pkgPath);
                            Log.Trace(pkgPath + " deleted.");
                        } catch (Exception ex) {
                            Log.Warn(ex, "Failed to delete delta package: " + l.FileName);
                        }
                    }
                }

                foreach (var l in Directory.EnumerateFiles(appPackageDir, "*.partial").ToArray()) {
                    try {
                        File.Delete(l);
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
            var targetPackage = filePathOverride == null
                ? new FileInfo(Path.Combine(Locator.PackagesDir!, release.FileName))
                : new FileInfo(filePathOverride);

            if (!targetPackage.Exists) {
                throw new ChecksumFailedException(targetPackage.FullName, "File doesn't exist.");
            }

            if (targetPackage.Length != release.Size) {
                throw new ChecksumFailedException(targetPackage.FullName, $"Size doesn't match ({targetPackage.Length} != {release.Size}).");
            }

            var hash = Utility.CalculateFileSHA1(targetPackage.FullName);
            if (!hash.Equals(release.SHA1, StringComparison.OrdinalIgnoreCase)) {
                throw new ChecksumFailedException(targetPackage.FullName, $"SHA1 doesn't match ({release.SHA1} != {hash}).");
            }
        }

        /// <summary>
        /// Throws an exception if the current application is not installed.
        /// </summary>
        protected virtual void EnsureInstalled()
        {
            if (AppId == null || !IsInstalled)
                throw new Exception("Cannot perform this operation in an application which is not installed.");
        }

        /// <summary>
        /// Acquires a globally unique mutex/lock for the current application, to avoid concurrent install/uninstall/update operations.
        /// </summary>
        protected virtual Mutex AcquireUpdateLock()
        {
            var mutexId = $"velopack-{AppId}";
            bool created = false;
            Mutex? mutex = null;
            try {
                mutex = new Mutex(false, mutexId, out created);
            } catch (Exception ex) {
                Log.Warn(ex, "Unable to acquire global mutex/lock.");
                created = false;
            }
            if (mutex == null || !created) {
                throw new Exception("Cannot perform this operation while another install/unistall operation is in progress.");
            }
            return mutex;
        }

        private static IUpdateSource CreateSimpleSource(string urlOrPath)
        {
            if (String.IsNullOrWhiteSpace(urlOrPath)) {
                throw new ArgumentException("Must pass a valid URL or file path to UpdateManager", nameof(urlOrPath));
            }
            if (Utility.IsHttpUrl(urlOrPath)) {
                return new SimpleWebSource(urlOrPath, Utility.CreateDefaultDownloader());
            } else {
                return new SimpleFileSource(new DirectoryInfo(urlOrPath));
            }
        }
    }
}
