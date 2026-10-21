using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Chunks;
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
    public class UpdateManager
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

        /// <summary>
        /// Returns a VelopackAsset if there is a local update prepared that requires a call to <see cref="ApplyUpdatesAndRestart(VelopackAsset, string[])"/> to be applied.
        /// This is either a downloaded full package or a staged chunk update, whichever has the highest version.
        /// </summary>
        public virtual VelopackAsset? UpdatePendingRestart {
            get {
                var current = CurrentVersion;
                if (current == null) return null;
                var pending = GetLatestPending(out _);
                if (pending != null && pending.Version > current)
                    return pending;
                return null;
            }
        }

        /// <summary> The currently installed app version when you created your release. Null if this is not a currently installed app. </summary>
        public virtual SemanticVersion? CurrentVersion => Locator.CurrentlyInstalledVersion;

        /// <summary>
        /// The release notes (markdown) of the currently installed version, as provided to 'vpk pack --releaseNotes'.
        /// Null if this is not a currently installed app, or the release was packed without release notes.
        /// </summary>
        public virtual string? CurrentReleaseNotesMarkdown => (Locator as VelopackLocator)?.CurrentReleaseNotesMarkdown;

        /// <summary>
        /// The release notes (rendered HTML) of the currently installed version, as provided to 'vpk pack --releaseNotes'.
        /// Null if this is not a currently installed app, or the release was packed without release notes.
        /// </summary>
        public virtual string? CurrentReleaseNotesHtml => (Locator as VelopackLocator)?.CurrentReleaseNotesHtml;

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

        /// <summary> If true, chunk-based updates were disabled by <see cref="UpdateOptions.DisableChunkedUpdates"/>. </summary>
        protected bool ShouldDisableChunkedUpdates { get; }

        // set when the installed updater turned out not to support 'assemble' (§7.4); the classic path then installs a new one
        private bool _chunkPathDisabled;

        /// <summary>
        /// Test hook: replaces the <c>Update.exe assemble</c> process. Receives the updater path and the exact argument list,
        /// reports progress 0-100 and returns the run result.
        /// </summary>
        internal Func<string, IReadOnlyList<string>, Action<int>, CancellationToken, Task<UpdateExeRunResult>>? AssembleRunner { get; set; }

        /// <summary> The retry policy used by the chunk fetch (tests inject the sleep function and clock). </summary>
        internal RetryPolicy ChunkRetryPolicy { get; set; } = new RetryPolicy();

        /// <summary> Test hook: the free-space query for the preflight (bytes available on the packages volume, or null to skip). </summary>
        internal Func<long?>? FreeSpaceQuery { get; set; }

        /// <summary>
        /// Creates a new UpdateManager instance which checks for updates using the Velopack Flow service (see <see cref="VelopackFlowSource"/>).
        /// </summary>
        /// <param name="options">Override / configure default update behaviors.</param>
        /// <param name="locator">This should usually be left null. Providing an <see cref="IVelopackLocator" /> allows you to mock up certain application paths.
        /// For example, if you wanted to test that updates are working in a unit test, you could provide an instance of <see cref="TestVelopackLocator"/>. </param>
        public UpdateManager(UpdateOptions? options = null, IVelopackLocator? locator = null)
            : this(new VelopackFlowSource(), options, locator)
        {
        }

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
        /// or it could be one of the predefined sources. (eg. <see cref="VelopackFlowSource"/>, <see cref="SimpleWebSource"/> or <see cref="GithubSource"/>, etc).</param>
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
            ShouldDisableChunkedUpdates = options?.DisableChunkedUpdates ?? false;
        }

        /// <inheritdoc cref="CheckForUpdatesAsync()"/>
        public UpdateInfo? CheckForUpdates()
        {
            return CheckForUpdatesAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Checks for updates, returning null if there are none available. If there are updates available, this method will return an
        /// UpdateInfo object containing the latest available release.
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
                return new UpdateInfo(latestRemoteFull, false);
            }

            // if the remote version is < than current version and downgrade is enabled
            if (latestRemoteFull.Version < installedVer && ShouldAllowVersionDowngrade) {
                Log.Info($"Latest remote release is older than current, and downgrade is enabled ({installedVer} -> {latestRemoteFull.Version}).");
                return new UpdateInfo(latestRemoteFull, true);
            }

            // if the remote version is the same as current version, and downgrade is enabled,
            // and we're searching for a different channel than current
            if (ShouldAllowVersionDowngrade && IsNonDefaultChannel) {
                if (SemanticVersion.CompareByVersion(latestRemoteFull.Version, installedVer) == 0) {
                    Log.Info(
                        $"Latest remote release is the same version of a different channel, and downgrade is enabled ({installedVer}: {DefaultChannel} -> {Channel}).");
                    return new UpdateInfo(latestRemoteFull, true);
                }
            }

            Log.Info(
                $"No updates, remote version ({latestRemoteFull.Version}) is not newer than current version ({installedVer}) and / or downgrade is not enabled.");
            return null;
        }

        /// <inheritdoc cref="DownloadUpdatesAsync(UpdateInfo, Action{int}, CancellationToken)"/>
        public void DownloadUpdates(UpdateInfo updates, Action<int>? progress = null)
        {
            DownloadUpdatesAsync(updates, progress)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Downloads the specified update to the local app packages directory. When the release and the installed application
        /// support chunk-based updates, only the parts of the package which differ from the installed files are downloaded and the
        /// new version is assembled locally; otherwise (or if that fails) the full package is downloaded. This function will acquire
        /// a global update lock so may fail if there is already another update operation in progress.
        /// </summary>
        /// <param name="updates">The updates to download. Should be retrieved from <see cref="CheckForUpdates"/>.</param>
        /// <param name="progress">The progress callback. Will be called with values from 0-100, never decreasing.</param>
        /// <param name="cancelToken">An optional cancellation token if you wish to stop this operation.</param>
        public virtual async Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null, CancellationToken cancelToken = default)
        {
            if (updates is null) {
                throw new ArgumentNullException(nameof(updates));
            }

            var targetRelease = updates.TargetFullRelease;
            if (targetRelease is null) {
                throw new ArgumentException(
                    $"Must pass a valid {nameof(UpdateInfo)} object with a non-null {nameof(UpdateInfo.TargetFullRelease)}",
                    nameof(updates));
            }

            var reporter = new ProgressReporter(progress ?? (_ => { }));

            EnsureInstalled();
            using var _mut = await AcquireUpdateLock().ConfigureAwait(false);

            var completeFile = Locator.GetLocalPackagePath(targetRelease);

            // a staged tree assembled for exactly this release is as good as a downloaded package
            if (StagedUpdate.FindReadyStagedFor(Locator, targetRelease) != null) {
                Log.Info($"A staged update for {targetRelease.FileName} is already assembled, nothing to do.");
                reporter.Report(100);
                return;
            }

            // if the package already exists on disk, we can skip the download.
            if (File.Exists(completeFile)) {
                Log.Info($"Package already exists on disk: '{completeFile}', nothing to do.");
                return;
            }

            var chunkSource = await GetChunkPathSourceAsync(targetRelease).ConfigureAwait(false);
            if (chunkSource != null) {
                try {
                    if (await DownloadChunkedAsync(targetRelease, chunkSource, reporter, cancelToken).ConfigureAwait(false)) {
                        return;
                    }
                } catch (OperationCanceledException) when (cancelToken.IsCancellationRequested) {
                    throw; // a real cancel: the staged directory is kept for resume
                } catch (Exception ex) {
                    Log.Warn(ex, "Chunk update failed, falling back to a full download.");
                    if (ex is ChunkIndexInvalidException || ex is AssembleInvalidException || ex is AssembleIoException || ex is StagingIoException) {
                        StagedUpdate.DeleteDir(Log, Locator.GetStagedDir(ChunkIndex.StagedIdOf(targetRelease.ChunkIndexSHA256)));
                    }
                    if (ex is AssembleUnsupportedException) {
                        Log.Warn("The installed Update.exe does not support chunk updates; they are disabled until an update installs a newer one.");
                        _chunkPathDisabled = true;
                    }
                }
            }

            await DownloadFullAsync(targetRelease, completeFile, reporter, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Evaluates the chunk path eligibility rules (§5.1). Returns the range-capable source when every rule holds, else null.
        /// </summary>
        private async Task<IRangeReadableUpdateSource?> GetChunkPathSourceAsync(VelopackAsset target)
        {
            try {
                if (!VelopackRuntimeInfo.IsWindows) return null;
                if (ShouldDisableChunkedUpdates) {
                    Log.Debug("Chunk updates are disabled by options.");
                    return null;
                }
                if (_chunkPathDisabled) {
                    Log.Debug("Chunk updates are disabled for this instance (the installed Update.exe lacks support).");
                    return null;
                }
                if (!target.HasChunkIndex) {
                    Log.Debug("The release carries no chunk index; downloading the full package.");
                    return null;
                }
                long offset = target.ChunkIndexOffset ?? -1;
                long length = target.ChunkIndexLength ?? 0;
                if (offset < 0 || length <= 0 || length > ChunkIndex.MaxTrailerLength || offset > target.Size - length) {
                    Log.Warn("The release's chunk index pointer is invalid; downloading the full package.");
                    return null;
                }
                if (!Hash256.TryParse(target.ChunkIndexSHA256, out _)) {
                    Log.Warn("The release's chunk index digest is invalid; downloading the full package.");
                    return null;
                }

                var appContent = Locator.AppContentDir;
                if (appContent == null || ChunkIndex.TryReadLocal(Path.Combine(appContent, ChunkIndex.LocalFileName)) == null) {
                    Log.Info("The installed application has no usable content map (sq.chunks); downloading the full package.");
                    return null;
                }

                if (Source is not IRangeReadableUpdateSource rangeSource) {
                    Log.Info("The update source does not support ranged reads; downloading the full package.");
                    return null;
                }

                var updateExe = Locator.UpdateExePath;
                var root = Locator.RootAppDir;
                if (string.IsNullOrEmpty(updateExe) || !File.Exists(updateExe) || string.IsNullOrEmpty(root)) {
                    Log.Info("Update.exe was not found; downloading the full package.");
                    return null;
                }

                // the updater vendored next to current/sq.chunks is the one that understands this index; a fallback
                // copy (LocalAppData, protected roots) may be stale, so refresh it from the root before relying on it
                var rootUpdateExe = Path.Combine(root!, "Update.exe");
                if (!PathUtil.FullPathEquals(updateExe!, rootUpdateExe) && File.Exists(rootUpdateExe)) {
                    bool differs = new FileInfo(rootUpdateExe).Length != new FileInfo(updateExe!).Length
                        || ChunkInflate.Sha256File(rootUpdateExe) != ChunkInflate.Sha256File(updateExe!);
                    if (differs) {
                        try {
                            Log.Info($"Refreshing fallback Update.exe '{updateExe}' from '{rootUpdateExe}'.");
                            await IoUtil.RetryAsync(() => {
                                File.Copy(rootUpdateExe, updateExe!, true);
                                return Task.CompletedTask;
                            }, logger: Log).ConfigureAwait(false);
                        } catch (Exception ex) {
                            Log.Warn(ex, "Failed to refresh the fallback Update.exe; downloading the full package.");
                            return null;
                        }
                    }
                }

                return rangeSource;
            } catch (Exception ex) {
                Log.Debug(ex, "Chunk update eligibility check failed; downloading the full package.");
                return null;
            }
        }

        /// <summary>
        /// The chunk path (§9.4): fetch index, plan, fetch ranges, assemble with Update.exe, install the sidecar updater.
        /// Returns false when the path was not attempted (nothing fetched) and the classic download should run quietly.
        /// </summary>
        private async Task<bool> DownloadChunkedAsync(VelopackAsset target, IRangeReadableUpdateSource rangeSource, ProgressReporter reporter,
            CancellationToken cancelToken)
        {
            var staged = StagedUpdate.ForAsset(Locator, target);
            Log.Info($"Downloading release {target.FileName} as chunks (staged id {staged.Id}).");

            using var reader = await rangeSource.OpenRangeReaderAsync(Log, target, cancelToken).ConfigureAwait(false);
            if (reader == null) {
                Log.Info("The update source returned no ranged reader for this release; downloading the full package.");
                return false;
            }

            StagedUpdate.DeleteStagedExcept(Locator, staged.Id);
            Directory.CreateDirectory(staged.Dir);

            var policy = ChunkRetryPolicy;
            var fetcher = new ChunkFetcher(Log, reader, target.Size, policy);

            // index (0-3 %)
            long indexOffset = target.ChunkIndexOffset!.Value;
            long indexLength = target.ChunkIndexLength!.Value;
            byte[] trailer;
            if (staged.HasVerifiedIndex(target.ChunkIndexSHA256)) {
                Log.Info("Reusing the previously fetched chunk index.");
                trailer = File.ReadAllBytes(staged.IndexPath);
            } else {
                trailer = await fetcher.FetchIndexAsync(
                    indexOffset,
                    indexLength,
                    target.ChunkIndexSHA256,
                    bytes => reporter.Report((int) (3 * bytes / indexLength)),
                    cancelToken).ConfigureAwait(false);
                staged.WriteIndex(trailer);
            }
            staged.WriteAssetJson(target);
            reporter.Report(3);

            ChunkIndex remote;
            try {
                remote = ChunkIndex.ParseTrailer(trailer, target.Size);
            } catch (InvalidDataException ex) {
                throw new ChunkIndexInvalidException(ex.Message, ex);
            }

            var local = ChunkIndex.TryReadLocal(Path.Combine(Locator.AppContentDir!, ChunkIndex.LocalFileName));
            var roots = new LocalRoots(Locator.AppContentDir!, Locator.UpdateExePath!, Locator.RootAppDir!);

            using (var blob = ChunkBlob.Open(staged.BlobPath)) {
                var plan = ChunkPlan.Create(remote, local, roots, blob);
                Log.Info(
                    $"Chunk plan: {plan.Needed.Count} chunks / {plan.NeededCompressedBytes} bytes to fetch of a {target.Size} byte package, " +
                    $"{plan.WholeFileCopies} files reused whole, {blob.Count} chunks already staged.");
                if (plan.ExceedsTransportCeiling(target.Size)) {
                    Log.Info("The chunk update would fetch most of the package; downloading the full package instead.");
                    return false;
                }
                if (!HasEnoughFreeSpace(plan)) {
                    return false;
                }

                // ranges (3-70 %)
                var requests = ChunkPlan.Coalesce(plan.Needed);
                long totalBytes = requests.Sum(r => r.Length);
                await fetcher.FetchRangesAsync(
                    requests,
                    blob,
                    false,
                    bytes => reporter.Report(3 + (int) (totalBytes == 0 ? 67 : 67 * bytes / totalBytes)),
                    cancelToken).ConfigureAwait(false);
                blob.Flush();
                reporter.Report(70);

                // assemble (70-97 %) with at most two extra rounds for hashes the assembler could not source locally
                for (int round = 0; ; round++) {
                    var result = await RunAssembleAsync(staged, p => reporter.Report(70 + (27 * Math.Max(0, Math.Min(100, p)) / 100)), cancelToken)
                        .ConfigureAwait(false);
                    if (result.ExitCode == 0) break;
                    switch (result.ExitCode) {
                    case 3:
                        if (round >= RetryPolicy.MaxMissingRounds) {
                            throw new AssembleFailedException($"Chunks were still missing after {round} extra fetch rounds.");
                        }
                        var hashes = staged.ReadMissingHashes();
                        var needed = ChunkPlan.ForHashes(remote, hashes);
                        if (needed.Count == 0) {
                            throw new AssembleFailedException("Update.exe reported missing chunks which the remote index does not contain.");
                        }
                        Log.Info($"Update.exe reported {hashes.Count} missing chunks; fetching them.");
                        await fetcher.FetchRangesAsync(ChunkPlan.Coalesce(needed), blob, true, null, cancelToken).ConfigureAwait(false);
                        blob.Flush();
                        File.Delete(staged.MissingPath);
                        break;
                    case 2:
                        throw new AssembleInvalidException($"Update.exe rejected the staged update: {result.ErrorMessage ?? "invalid input"}");
                    case 4:
                        throw new AssembleIoException($"Update.exe could not write the staged update: {result.ErrorMessage ?? "I/O error"}");
                    default:
                        if (!result.SawProtocolLine) {
                            throw new AssembleUnsupportedException($"Update.exe exited with {result.ExitCode} without any protocol output.");
                        }
                        var message = result.ErrorMessage ?? "unknown error";
                        throw new AssembleFailedException($"Update.exe assemble exited with {result.ExitCode}: {message}");
                    }
                }
            }
            reporter.Report(97);

            var ready = staged.TryReadReady();
            if (ready == null) {
                throw new AssembleFailedException("Update.exe assemble succeeded but did not leave a valid ready.json.");
            }
            if (!string.Equals(ready.Marker.IndexSHA256, target.ChunkIndexSHA256, StringComparison.OrdinalIgnoreCase)) {
                throw new AssembleInvalidException("The ready marker was written for a different chunk index.");
            }

            var expectedUpdater = Hash256.Parse(ready.Marker.UpdaterSHA256);
            if (!File.Exists(staged.UpdaterPath) || ChunkInflate.Sha256File(staged.UpdaterPath) != expectedUpdater) {
                throw new AssembleIoException("The staged Update.exe does not match the ready marker.");
            }

            // install the target's updater now, best-effort; ApplyStaged repeats this and fails if it cannot
            try {
                var updateExe = Locator.UpdateExePath!;
                if (!File.Exists(updateExe) || ChunkInflate.Sha256File(updateExe) != expectedUpdater) {
                    Log.Info("Installing the new Update.exe to " + updateExe);
                    await IoUtil.RetryAsync(() => {
                        File.Copy(staged.UpdaterPath, updateExe, true);
                        return Task.CompletedTask;
                    }, logger: Log).ConfigureAwait(false);
                }
            } catch (Exception ex) {
                Log.Warn(ex, "Failed to install the new Update.exe; it will be installed when the update is applied.");
            }

            CleanPackagesExcept(null);
            Log.Info($"Chunk update complete. Staged update tree: {staged.Dir}");
            reporter.Report(100);
            return true;
        }

        /// <summary>
        /// Free-space preflight (§6.6): the staged tree is one plaintext copy of the app plus the fetched chunks. A failed
        /// query counts as "enough" (best effort); only a successful query below the requirement selects the classic path.
        /// </summary>
        private bool HasEnoughFreeSpace(ChunkPlan plan)
        {
            long required = (long) (1.1 * (plan.RemoteFileBytes + plan.NeededPlaintextBytes)) + (64L * 1024 * 1024);
            long? free;
            try {
                free = FreeSpaceQuery != null ? FreeSpaceQuery() : QueryFreeSpace(Locator.PackagesDir!);
            } catch (Exception ex) {
                Log.Debug(ex, "Could not determine the free disk space; assuming enough.");
                return true;
            }
            if (free.HasValue && free.Value < required) {
                Log.Warn($"Not enough free space for a chunk update ({free.Value} available, {required} required); downloading the full package.");
                return false;
            }
            return true;
        }

        private static long? QueryFreeSpace(string directory)
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrEmpty(root)) return null;
            return new DriveInfo(root!).AvailableFreeSpace;
        }

        /// <summary>
        /// Runs <c>Update.exe --silent assemble --staged &lt;dir&gt; --parentPid &lt;pid&gt; --rootDir &lt;root&gt; --packageDir &lt;pkgs&gt;</c>.
        /// </summary>
        private Task<UpdateExeRunResult> RunAssembleAsync(StagedUpdate staged, Action<int> progress, CancellationToken cancelToken)
        {
            var updateExe = Locator.UpdateExePath!;
            // the watchdog must follow the process that really holds the packages lock, never a mocked process id
            int pid;
            using (var self = System.Diagnostics.Process.GetCurrentProcess()) pid = self.Id;
            var args = new List<string> {
                "--silent",
                "assemble",
                "--staged", staged.Dir,
                "--parentPid", pid.ToString(),
                "--rootDir", Locator.RootAppDir!,
                "--packageDir", Locator.PackagesDir!,
            };
            if (AssembleRunner != null) {
                return AssembleRunner(updateExe, args, progress, cancelToken);
            }
            return new UpdateExeRunner(Log).RunAsync(updateExe, args, progress, cancelToken);
        }

        /// <summary>The classic path: download the whole package, verify it, extract Update.exe, clean up.</summary>
        private async Task DownloadFullAsync(VelopackAsset targetRelease, string completeFile, ProgressReporter reporter,
            CancellationToken cancelToken)
        {
            var incompleteFile = completeFile + ".partial";
            int baseProgress = reporter.Last;

            void reportProgress(int x)
            {
                // progress never goes backwards: the classic 0-100 is mapped onto [last, 100]
                reporter.Report(baseProgress + ((100 - baseProgress) * Math.Max(0, Math.Min(100, x)) / 100));
            }

            try {
                Log.Info($"Downloading full release ({targetRelease.FileName})");
                File.Delete(incompleteFile);
                await Source.DownloadReleaseEntry(Log, targetRelease, incompleteFile, reportProgress, cancelToken).ConfigureAwait(false);
                Log.Info("Verifying package checksum...");
                await VerifyPackageChecksumAsync(targetRelease, incompleteFile).ConfigureAwait(false);

                IoUtil.MoveFile(incompleteFile, completeFile, true);
                Log.Info("Full release download complete. Package moved to: " + completeFile);
                reporter.Report(100);
            } finally {
                if (VelopackRuntimeInfo.IsWindows && !cancelToken.IsCancellationRequested && File.Exists(completeFile)) {
                    try {
                        var updateExe = Locator.UpdateExePath;
                        if (updateExe == null) {
                            throw new InvalidOperationException("Update.exe path is null, cannot extract new Update.exe.");
                        }
                        Log.Info("Extracting new Update.exe to " + updateExe);
                        var zip = new ZipPackage(completeFile, loadUpdateExe: true);

                        if (zip.UpdateExeBytes == null) {
                            Log.Error("Update.exe not found in package, skipping extraction.");
                        } else {
                            await IoUtil.RetryAsync(async () => {
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

            // a downloaded package supersedes every staged tree
            StagedUpdate.DeleteStagedExcept(Locator, null);
        }

        /// <summary>
        /// This will exit your app immediately, apply updates, and then optionally relaunch the app using the specified
        /// restart arguments. If you need to save state or clean up, you should do that before calling this method.
        /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
        /// You can check if there are pending updates by checking <see cref="UpdatePendingRestart"/>.
        /// </summary>
        /// <param name="toApply">The target release to apply. Can be left null to auto-apply the newest downloaded release.</param>
        /// <param name="restartArgs">The arguments to pass to the application when it is restarted.</param>
        public void ApplyUpdatesAndRestart(VelopackAsset? toApply, string[]? restartArgs = null)
        {
            WaitExitThenApplyUpdates(toApply, silent: false, restart: true, restartArgs);
            Locator.Process.Exit(0);
        }

        /// <summary>
        /// This will exit your app immediately, apply updates, and then optionally relaunch the app using the specified
        /// restart arguments. If you need to save state or clean up, you should do that before calling this method.
        /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
        /// You can check if there are pending updates by checking <see cref="UpdatePendingRestart"/>.
        /// </summary>
        /// <param name="toApply">The target release to apply. Can be left null to auto-apply the newest downloaded release.</param>
        public void ApplyUpdatesAndExit(VelopackAsset? toApply)
        {
            WaitExitThenApplyUpdates(toApply, silent: true, restart: false);
            Locator.Process.Exit(0);
        }

        /// <summary>
        /// This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
        /// You should then clean up any state and exit your app. The updater will apply updates and then
        /// optionally restart your app. The updater will only wait for 60 seconds before giving up.
        /// You can check if there are pending updates by checking <see cref="UpdatePendingRestart"/>.
        /// </summary>
        /// <param name="toApply">The target release to apply. Can be left null to auto-apply the newest downloaded release.</param>
        /// <param name="silent">Configure whether Velopack should show a progress window / dialogs during the updates or not.</param>
        /// <param name="restart">Configure whether Velopack should restart the app after the updates have been applied.</param>
        /// <param name="restartArgs">The arguments to pass to the application when it is restarted.</param>
        public void WaitExitThenApplyUpdates(VelopackAsset? toApply, bool silent = false, bool restart = true, string[]? restartArgs = null)
        {
            var pid = Locator.Process.GetCurrentProcessId();
            if (toApply != null) {
                var ready = StagedUpdate.FindReadyStagedFor(Locator, toApply);
                if (ready != null) {
                    UpdateExe.ApplyStaged(Locator, ready.Dir, silent, pid, restart, restartArgs);
                } else {
                    UpdateExe.Apply(Locator, toApply, silent, pid, restart, restartArgs);
                }
                return;
            }

            var pending = GetLatestPending(out var staged);
            if (staged != null) {
                UpdateExe.ApplyStaged(Locator, staged.Dir, silent, pid, restart, restartArgs);
            } else {
                UpdateExe.Apply(Locator, pending, silent, pid, restart, restartArgs);
            }
        }

        /// <summary>
        /// The newest update prepared locally: the highest version among the ready staged trees and the downloaded full
        /// packages. <paramref name="staged"/> is set when the winner is a staged tree.
        /// </summary>
        private VelopackAsset? GetLatestPending(out StagedReady? staged)
        {
            staged = StagedUpdate.FindReadyStaged(Locator);
            VelopackAsset? best = staged?.Asset;
            var latestLocal = Locator.GetLatestLocalFullPackage();
            if (latestLocal != null && (best == null || latestLocal.Version > best.Version)) {
                best = latestLocal;
                staged = null;
            }
            return best;
        }

        /// <summary>
        /// Removes any incomplete files (.partial) and packages (.nupkg) from the packages directory that does not match
        /// the provided asset. If assetToKeep is null, all packages will be deleted.
        /// </summary>
        protected virtual void CleanPackagesExcept(string? assetToKeep)
        {
            try {
                Log.Info("Cleaning up old and incomplete packages from packages directory.");

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
                Log.Warn(ex, "Failed to clean up old and incomplete packages.");
            }
        }

        /// <summary>
        /// Check a package checksum against the one in the release entry, and throws if the checksum does not match.
        /// </summary>
        /// <param name="release">The entry to check</param>
        /// <param name="filePathOverride">Optional file path, if not specified the package will be loaded from %pkgdir%/release.OriginalFilename.</param>
        protected internal virtual async Task VerifyPackageChecksumAsync(VelopackAsset release, string? filePathOverride = null)
        {
            var targetPackage = new FileInfo(filePathOverride ?? Locator.GetLocalPackagePath(release));

            if (!targetPackage.Exists) {
                throw new ChecksumFailedException(targetPackage.FullName, "File doesn't exist.");
            }

            if (targetPackage.Length != release.Size) {
                throw new ChecksumFailedException(targetPackage.FullName, $"Size doesn't match ({targetPackage.Length} != {release.Size}).");
            }

            // SHA1 is required and should always be present in the remote feed.
            // SHA256 is optional and only supported by newer clients, so if it's
            // missing from the remote asset we fall back to verifying SHA1 only.
            var (sha1, sha256) = await IoUtil.CalculateFileSHA1AndSHA256Async(targetPackage.FullName).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(release.SHA256)) {
                if (!sha256.Equals(release.SHA256, StringComparison.Ordinal)) {
                    throw new ChecksumFailedException(targetPackage.FullName, $"SHA256 doesn't match ({release.SHA256} != {sha256}).");
                }
            } else {
                if (!sha1.Equals(release.SHA1, StringComparison.OrdinalIgnoreCase)) {
                    throw new ChecksumFailedException(targetPackage.FullName, $"SHA1 doesn't match ({release.SHA1} != {sha1}).");
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
            if (Locator.PackagesDir is null) {
                throw new InvalidOperationException(
                    $"Cannot acquire update lock, {nameof(IVelopackLocator)}.{nameof(IVelopackLocator.PackagesDir)} is not set.");
            }

            var dir = Directory.CreateDirectory(Locator.PackagesDir!);
            var lockPath = Path.Combine(dir.FullName, ".velopack_lock");
            var fsLock = new LockFile(lockPath);
            await fsLock.LockAsync().ConfigureAwait(false);
            return fsLock;
        }

        private static IUpdateSource CreateSimpleSource(string urlOrPath)
        {
            if (string.IsNullOrWhiteSpace(urlOrPath)) {
                throw new ArgumentException("Must pass a valid URL or file path to UpdateManager", nameof(urlOrPath));
            }

            if (HttpUtil.IsHttpUrl(urlOrPath)) {
                return new SimpleWebSource(urlOrPath, HttpUtil.CreateDefaultDownloader());
            } else {
                return new SimpleFileSource(new DirectoryInfo(urlOrPath));
            }
        }

        /// <summary>
        /// Debounces progress to even integers, reports only changes, and never reports a value lower than the last one.
        /// The progress delegate may very likely invoke into the client main thread for UI updates, so this reduces the spam.
        /// Safe to call from several threads.
        /// </summary>
        private sealed class ProgressReporter
        {
            private readonly Action<int> _progress;
            private readonly object _lock = new object();
            private int _last;

            public ProgressReporter(Action<int> progress)
            {
                _progress = progress;
            }

            /// <summary>The last value reported (0 before the first report).</summary>
            public int Last {
                get {
                    lock (_lock) return _last;
                }
            }

            public void Report(int x)
            {
                x = Math.Max(0, Math.Min(100, x));
                int result = (int) (Math.Round(x / 2d, MidpointRounding.AwayFromZero) * 2d);
                lock (_lock) {
                    if (result <= _last) return;
                    _last = result;
                    // invoked under the lock so concurrent fetch workers can never deliver values out of order
                    _progress(result);
                }
            }
        }
    }
}
