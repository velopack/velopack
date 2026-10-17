using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Chunks
{
    /// <summary>A staged directory whose <c>ready.json</c> parsed and is consistent with its directory name.</summary>
    internal sealed class StagedReady
    {
        public string Dir { get; }
        public string Id { get; }
        public StagedReadyMarker Marker { get; }
        public VelopackAsset Asset => Marker.Asset;

        public StagedReady(string dir, string id, StagedReadyMarker marker)
        {
            Dir = dir;
            Id = id;
            Marker = marker;
        }
    }

    /// <summary>
    /// The on-disk staging layout of one chunk update (§6.1): <c>{PackagesDir}/staged/&lt;staged id&gt;/</c> with the fetched
    /// index, the chunk blob, the assembled tree and the completion marker. This class is the only place that scans for
    /// ready staged trees and garbage-collects staged directories.
    /// </summary>
    internal sealed class StagedUpdate
    {
        public const string StagedRootDirName = "staged";
        public const string AssetFileName = "asset.json";
        public const string IndexFileName = "index.bin";
        public const string BlobFileName = ChunkBlob.FileName;
        public const string MissingFileName = "missing.bin";
        public const string ReadyFileName = "ready.json";
        public const string CurrentDirName = "current";
        public const string UpdaterFileName = "Update.exe";
        public const string StubsDirName = "stubs";
        public static readonly TimeSpan IncompleteMaxAge = TimeSpan.FromDays(7);

        public string Dir { get; }
        public string Id { get; }
        public string AssetPath => Path.Combine(Dir, AssetFileName);
        public string IndexPath => Path.Combine(Dir, IndexFileName);
        public string BlobPath => Path.Combine(Dir, BlobFileName);
        public string MissingPath => Path.Combine(Dir, MissingFileName);
        public string ReadyPath => Path.Combine(Dir, ReadyFileName);
        public string CurrentDir => Path.Combine(Dir, CurrentDirName);
        public string UpdaterPath => Path.Combine(Dir, UpdaterFileName);
        public string StubsDir => Path.Combine(Dir, StubsDirName);

        public StagedUpdate(string dir, string id)
        {
            Dir = dir ?? throw new ArgumentNullException(nameof(dir));
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        /// <summary>The staging directory for an asset carrying a chunk index pointer (§2.4 identity).</summary>
        public static StagedUpdate ForAsset(IVelopackLocator locator, VelopackAsset asset)
        {
            if (!asset.HasChunkIndex) throw new ArgumentException("The asset has no chunk index pointer.", nameof(asset));
            var id = ChunkIndex.StagedIdOf(asset.ChunkIndexSHA256);
            return new StagedUpdate(locator.GetStagedDir(id), id);
        }

        /// <summary>Writes <c>asset.json</c> (the plain §2.6 shape) atomically.</summary>
        public void WriteAssetJson(VelopackAsset asset)
        {
            Directory.CreateDirectory(Dir);
            WriteAtomic(AssetPath, System.Text.Encoding.UTF8.GetBytes(CompiledJson.SerializeVelopackAsset(asset.ToPlainAsset())));
        }

        /// <summary>Writes <c>ready.json</c> atomically (normally <c>Update.exe assemble</c> writes the marker; used by tests).</summary>
        public void WriteReadyMarker(StagedReadyMarker marker)
        {
            Directory.CreateDirectory(Dir);
            WriteAtomic(ReadyPath, System.Text.Encoding.UTF8.GetBytes(CompiledJson.SerializeStagedReadyMarker(marker)));
        }

        /// <summary>Reads the hashes assembly could not source (<c>missing.bin</c> = N × 32 bytes).</summary>
        public List<Hash256> ReadMissingHashes()
        {
            var result = new List<Hash256>();
            if (!File.Exists(MissingPath)) return result;
            var bytes = File.ReadAllBytes(MissingPath);
            for (int o = 0; o + Hash256.Size <= bytes.Length; o += Hash256.Size) {
                result.Add(Hash256.FromBytes(bytes, o));
            }
            return result;
        }

        /// <summary>True when <c>index.bin</c> exists and hashes to the expected digest (resume).</summary>
        public bool HasVerifiedIndex(string expectedSha256)
        {
            try {
                if (!File.Exists(IndexPath) || !Hash256.TryParse(expectedSha256, out var expected)) return false;
                return ChunkInflate.Sha256File(IndexPath) == expected;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>Writes <c>index.bin</c> atomically.</summary>
        public void WriteIndex(byte[] trailer)
        {
            Directory.CreateDirectory(Dir);
            WriteAtomic(IndexPath, trailer);
        }

        /// <summary>The marker of this directory if it is ready and consistent, otherwise null.</summary>
        public StagedReady? TryReadReady() => TryReadReady(Dir);

        /// <summary>
        /// Parses a directory's <c>ready.json</c>; returns null unless it parses with <c>Format == 1</c>, its
        /// <c>StagedId</c> equals the directory name and the first 32 hex characters of its <c>IndexSHA256</c>,
        /// and its asset has a version.
        /// </summary>
        public static StagedReady? TryReadReady(string dir)
        {
            try {
                var readyPath = Path.Combine(dir, ReadyFileName);
                if (!File.Exists(readyPath)) return null;
                var marker = CompiledJson.DeserializeStagedReadyMarker(File.ReadAllText(readyPath, System.Text.Encoding.UTF8));
                if (marker == null || marker.Format != StagedReadyMarker.CurrentFormat) return null;
                if (string.IsNullOrEmpty(marker.StagedId) || string.IsNullOrEmpty(marker.IndexSHA256)) return null;
                if (!Hash256.TryParse(marker.IndexSHA256, out _)) return null;
                var dirName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.Equals(marker.StagedId, dirName, StringComparison.Ordinal)) return null;
                if (!string.Equals(marker.StagedId, ChunkIndex.StagedIdOf(marker.IndexSHA256), StringComparison.Ordinal)) return null;
                if (marker.Asset == null || marker.Asset.Version == null) return null;
                if (string.IsNullOrEmpty(marker.UpdaterSHA256) || !Hash256.TryParse(marker.UpdaterSHA256, out _)) return null;
                return new StagedReady(dir, marker.StagedId, marker);
            } catch (Exception) {
                return null;
            }
        }

        /// <summary>Every ready staged tree under <c>PackagesDir/staged</c>.</summary>
        public static List<StagedReady> FindAllReadyStaged(IVelopackLocator locator)
        {
            var result = new List<StagedReady>();
            try {
                var root = locator.GetStagedRootDir();
                if (root == null || !Directory.Exists(root)) return result;
                foreach (var dir in Directory.EnumerateDirectories(root)) {
                    var ready = TryReadReady(dir);
                    if (ready != null) result.Add(ready);
                }
            } catch (Exception ex) {
                locator.Log.Warn(ex, "Failed to enumerate staged updates.");
            }
            return result;
        }

        /// <summary>The ready staged tree with the highest version, or null.</summary>
        public static StagedReady? FindReadyStaged(IVelopackLocator locator)
        {
            return FindAllReadyStaged(locator).OrderByDescending(r => r.Asset.Version).FirstOrDefault();
        }

        /// <summary>The ready staged tree matching the asset's staged id and index digest (§2.4), or null.</summary>
        public static StagedReady? FindReadyStagedFor(IVelopackLocator locator, VelopackAsset asset)
        {
            if (asset == null || !asset.HasChunkIndex) return null;
            try {
                var id = ChunkIndex.StagedIdOf(asset.ChunkIndexSHA256);
                var ready = TryReadReady(locator.GetStagedDir(id));
                if (ready == null) return null;
                if (!string.Equals(ready.Marker.IndexSHA256, asset.ChunkIndexSHA256, StringComparison.OrdinalIgnoreCase)) return null;
                return ready;
            } catch (Exception) {
                return null;
            }
        }

        /// <summary>Deletes every <c>staged/*</c> directory except <paramref name="keepId"/> (null keeps nothing).</summary>
        public static void DeleteStagedExcept(IVelopackLocator locator, string? keepId)
        {
            try {
                var root = locator.GetStagedRootDir();
                if (root == null || !Directory.Exists(root)) return;
                foreach (var dir in Directory.GetDirectories(root)) {
                    if (keepId != null && string.Equals(Path.GetFileName(dir), keepId, StringComparison.Ordinal)) continue;
                    DeleteDir(locator.Log, dir);
                }
            } catch (Exception ex) {
                locator.Log.Warn(ex, "Failed to clean up staged updates.");
            }
        }

        /// <summary>Deletes one staged directory, best effort.</summary>
        public static void DeleteDir(IVelopackLogger log, string dir)
        {
            try {
                if (!Directory.Exists(dir)) return;
                log.Info($"Deleting staged update directory: {dir}");
                IoUtil.DeleteFileOrDirectoryHard(dir, throwOnFailure: true, renameFirst: false, logger: log);
            } catch (Exception ex) {
                log.Warn(ex, $"Failed to delete staged directory '{dir}'.");
            }
        }

        /// <summary>
        /// Startup garbage collection (§6.6): keeps the highest-version ready tree, deletes every other ready tree and
        /// every incomplete tree whose newest fetch artifact is older than seven days. The caller holds the packages lock.
        /// </summary>
        public static void CleanupOnStartup(IVelopackLocator locator, Func<DateTime>? clock = null)
        {
            try {
                var root = locator.GetStagedRootDir();
                if (root == null || !Directory.Exists(root)) return;
                var now = clock?.Invoke() ?? DateTime.UtcNow;
                var ready = new List<StagedReady>();
                var incomplete = new List<string>();
                foreach (var dir in Directory.GetDirectories(root)) {
                    var r = TryReadReady(dir);
                    if (r != null) ready.Add(r);
                    else incomplete.Add(dir);
                }

                var keep = ready.OrderByDescending(r => r.Asset.Version).FirstOrDefault();
                foreach (var r in ready) {
                    if (ReferenceEquals(r, keep)) continue;
                    DeleteDir(locator.Log, r.Dir);
                }

                foreach (var dir in incomplete) {
                    DateTime newest = DateTime.MinValue;
                    foreach (var name in new[] { IndexFileName, BlobFileName, AssetFileName }) {
                        var file = Path.Combine(dir, name);
                        if (File.Exists(file)) {
                            var t = File.GetLastWriteTimeUtc(file);
                            if (t > newest) newest = t;
                        }
                    }
                    if (now - newest > IncompleteMaxAge) {
                        DeleteDir(locator.Log, dir);
                    }
                }
            } catch (Exception ex) {
                locator.Log.Warn(ex, "Failed to clean up staged updates.");
            }
        }

        private static void WriteAtomic(string path, byte[] content)
        {
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                fs.Write(content, 0, content.Length);
                fs.Flush(true);
            }
            IoUtil.MoveFile(tmp, path, true);
        }
    }
}
