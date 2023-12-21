using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using Squirrel.Compression;
using Squirrel.Locators;
using Squirrel.Sources;

namespace Squirrel
{
    public class UpdateManager
    {
        public virtual string AppId => Locator.AppId;

        public virtual bool IsInstalled => Locator.CurrentlyInstalledVersion != null;

        public virtual SemanticVersion CurrentVersion => Locator.CurrentlyInstalledVersion;

        protected IUpdateSource Source { get; }

        protected ILogger Log { get; }

        protected ISquirrelLocator Locator { get; }

        public UpdateManager(string urlOrPath, string channel = null, ILogger logger = null)
            : this(urlOrPath, channel, logger, null)
        {
        }

        public UpdateManager(string urlOrPath, string channel = null, ILogger logger = null, ISquirrelLocator locator = null)
            : this(CreateSimpleSource(urlOrPath, channel, logger), logger, locator)
        {
        }

        public UpdateManager(IUpdateSource source, ILogger logger = null)
            : this(source, logger, null)
        {
        }

        public UpdateManager(IUpdateSource source, ILogger logger = null, ISquirrelLocator locator = null)
        {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }
            Source = source;
            Log = logger ?? NullLogger.Instance;
            Locator = locator ?? SquirrelLocator.GetDefault(Log);
        }

        public UpdateInfo CheckForUpdates()
        {
            return CheckForUpdatesAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public virtual async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancelToken = default)
        {
            EnsureInstalled();
            var installedVer = CurrentVersion;
            var betaId = Locator.GetOrCreateStagedUserId();
            var latestLocalFull = Locator.GetLatestLocalPackage();

            Log.Debug("Retrieving latest release feed.");
            var feed = await Source.GetReleaseFeed(betaId, latestLocalFull?.Identity).ConfigureAwait(false);

            var latestRemoteFull = feed.Where(r => !r.IsDelta).MaxBy(x => x.Version).FirstOrDefault();
            if (latestRemoteFull == null) {
                Log.Info("No remote releases found.");
                return null;
            }

            if (latestRemoteFull.Version <= installedVer) {
                Log.Info($"No updates, remote version ({latestRemoteFull.Version}) is not newer than current version ({installedVer}).");
                return null;
            }

            Log.Info($"Found remote update available ({latestRemoteFull.Version}).");

            var matchingRemoteDelta = feed.Where(r => r.IsDelta && r.Version == latestRemoteFull.Version).FirstOrDefault();
            if (matchingRemoteDelta == null) {
                Log.Info($"Unable to find delta matching version {latestRemoteFull.Version}, only full update will be available.");
                return new UpdateInfo(latestRemoteFull);
            }

            // if we have a local full release, we try to apply delta's from that version to target version.
            // if we do not have a local release, we try to apply delta's to a copy of the current installed app files.
            SemanticVersion deltaFromVer = null;
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

            var deltas = feed.Where(r => r.IsDelta && r.Version > deltaFromVer && r.Version <= latestRemoteFull.Version).ToArray();
            Log.Debug($"Found {deltas.Length} delta releases between {deltaFromVer} and {latestRemoteFull.Version}.");
            return new UpdateInfo(latestRemoteFull, latestLocalFull, deltas);
        }

        public void DownloadAndPrepareUpdates(UpdateInfo updates, Action<int> progress = null, bool ignoreDeltas = false)
        {
            DownloadAndPrepareUpdatesAsync(updates, progress, ignoreDeltas)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public virtual async Task DownloadAndPrepareUpdatesAsync(
            UpdateInfo updates, Action<int> progress = null, bool ignoreDeltas = false, CancellationToken cancelToken = default)
        {
            progress ??= (_ => { });
            var targetRelease = updates?.TargetFullRelease;
            if (targetRelease == null) {
                throw new ArgumentException("Must pass a valid UpdateInfo object with a non-null TargetFullRelease", nameof(updates));
            }

            EnsureInstalled();
            using var _mut = AcquireUpdateLock();

            var completeFile = Path.Combine(Locator.PackagesDir, targetRelease.OriginalFilename);
            var incompleteFile = completeFile + ".downloading";

            if (File.Exists(completeFile)) {
                Log.Info($"Package already exists on disk: '{completeFile}', verifying checksum...");
                try {
                    VerifyPackageChecksum(targetRelease);
                } catch (ChecksumFailedException ex) {
                    Log.Warn(ex, $"Checksum failed for file '{completeFile}'. Deleting and starting over.");
                }
            }

            var deltasSize = updates.DeltasToTarget.Sum(x => x.Filesize);
            var deltasCount = updates.DeltasToTarget.Count();

            try {
                if (deltasCount > 0) {
                    if (ignoreDeltas) {
                        Log.Info("Ignoring delta updates (ignoreDeltas parameter)");
                    } else {
                        if (deltasSize > 10 || deltasSize > targetRelease.Filesize) {
                            Log.Info($"There are too many delta's ({deltasCount} > 10) or the sum of their size ({deltasSize}) is too large. " +
                                $"Only full update will be available.");
                        } else {
                            var _1 = Utility.GetTempDirectory(out var deltaStagingDir, Locator.AppTempDir);
                            if (updates.BaseRelease?.OriginalFilename != null) {
                                string basePackagePath = Path.Combine(Locator.PackagesDir, updates.BaseRelease.OriginalFilename);
                                if (!File.Exists(basePackagePath))
                                    throw new Exception($"Unable to find base package {basePackagePath} for delta update.");
                                EasyZip.ExtractZipToDirectory(Log, basePackagePath, deltaStagingDir);
                            } else {
                                Log.Warn("No base package available. Attempting delta update using application files.");
                                Utility.CopyFiles(Locator.AppContentDir, deltaStagingDir);
                            }
                            progress(10);
                            await DownloadAndApplyDeltaUpdates(deltaStagingDir, updates, x => Utility.CalculateProgress(x, 10, 90))
                                .ConfigureAwait(false);
                            progress(90);

                            Log.Info("Delta updates completed, creating final update package.");
                            File.Delete(incompleteFile);
                            EasyZip.CreateZipFromDirectory(Log, incompleteFile, deltaStagingDir);
                            File.Delete(completeFile);
                            File.Move(incompleteFile, completeFile);
                            Log.Info("Delta release preparations complete. Package moved to: " + completeFile);
                            progress(100);
                            return; // success!
                        }
                    }
                }
            } catch (Exception ex) {
                Log.Warn(ex, "Unable to apply delta updates, falling back to full update.");
            }

            Log.Info($"Downloading full release ({targetRelease.OriginalFilename})");
            File.Delete(incompleteFile);
            await Source.DownloadReleaseEntry(targetRelease, incompleteFile, progress).ConfigureAwait(false);
            Log.Info("Verifying package checksum...");
            VerifyPackageChecksum(targetRelease, incompleteFile);
            File.Delete(completeFile);
            File.Move(incompleteFile, completeFile);
            Log.Info("Full release download complete. Package moved to: " + completeFile);
        }

        protected virtual async Task DownloadAndApplyDeltaUpdates(string extractedBasePackage, UpdateInfo updates, Action<int> progress)
        {
            var packagesDirectory = Locator.PackagesDir;
            var releasesToDownload = updates.DeltasToTarget.OrderBy(d => d.Version).ToArray();

            // downloading accounts for 0%-50% of progress
            double current = 0;
            double toIncrement = 100.0 / releasesToDownload.Count();
            await releasesToDownload.ForEachAsync(async x => {
                var targetFile = Path.Combine(packagesDirectory, x.OriginalFilename);
                double component = 0;
                Log.Debug($"Downloading delta version {x.Version}");
                await Source.DownloadReleaseEntry(x, targetFile, p => {
                    lock (progress) {
                        current -= component;
                        component = toIncrement / 100.0 * p;
                        var progressOfStep = (int) Math.Round(current += component);
                        progress(Utility.CalculateProgress(progressOfStep, 0, 50));
                    }
                }).ConfigureAwait(false);
                VerifyPackageChecksum(x);
                Log.Debug($"Download complete for delta version {x.Version}");
            }).ConfigureAwait(false);

            Log.Info("All delta packages downloaded and verified, applying them to the base now. The delta staging dir is: " + extractedBasePackage);

            // applying deltas accounts for 50%-100% of progress
            double progressStepSize = 100d / releasesToDownload.Length;
            var builder = new DeltaPackage(Log, Locator.AppTempDir);
            for (var i = 0; i < releasesToDownload.Length; i++) {
                var rel = releasesToDownload[i];
                double baseProgress = i * progressStepSize;
                var packageFile = Path.Combine(packagesDirectory, rel.OriginalFilename);
                builder.ApplyDeltaPackageFast(extractedBasePackage, packageFile, x => {
                    var progressOfStep = (int) (baseProgress + (progressStepSize * (x / 100d)));
                    progress(Utility.CalculateProgress(progressOfStep, 50, 100));
                });
            }

            progress(100);
        }

        protected virtual void VerifyPackageChecksum(ReleaseEntry release, string filePathOverride = null)
        {
            var targetPackage = filePathOverride == null
                ? new FileInfo(Path.Combine(Locator.PackagesDir, release.OriginalFilename))
                : new FileInfo(filePathOverride);

            if (!targetPackage.Exists) {
                throw new ChecksumFailedException(targetPackage.FullName, "File doesn't exist.");
            }

            if (targetPackage.Length != release.Filesize) {
                targetPackage.Delete();
                throw new ChecksumFailedException(targetPackage.FullName, $"Size doesn't match ({targetPackage.Length} != {release.Filesize}).");
            }

            var hash = Utility.CalculateFileSHA1(targetPackage.FullName);
            if (!hash.Equals(release.SHA1, StringComparison.OrdinalIgnoreCase)) {
                targetPackage.Delete();
                throw new ChecksumFailedException(targetPackage.FullName, $"SHA1 doesn't match ({release.SHA1} != {hash}).");
            }
        }

        protected virtual void EnsureInstalled()
        {
            if (AppId == null || !IsInstalled)
                throw new Exception("Cannot perform this operation in an application which is not installed.");
        }

        protected virtual Mutex AcquireUpdateLock()
        {
            var mutexId = $"clowdsquirrel-{AppId}";
            bool created = false;
            Mutex mutex = null;
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

        private static IUpdateSource CreateSimpleSource(string urlOrPath, string channel, ILogger logger)
        {
            logger ??= NullLogger.Instance;
            if (String.IsNullOrWhiteSpace(urlOrPath)) {
                throw new ArgumentException("Must pass a valid URL or file path to UpdateManager", nameof(urlOrPath));
            }
            if (Utility.IsHttpUrl(urlOrPath)) {
                return new SimpleWebSource(urlOrPath, channel, Utility.CreateDefaultDownloader(), logger);
            } else {
                return new SimpleFileSource(new DirectoryInfo(urlOrPath), channel, logger);
            }
        }
    }
}
