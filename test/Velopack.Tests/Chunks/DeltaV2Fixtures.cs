#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Velopack.Chunks;

namespace Velopack.Tests.Chunks;

/// <summary>
/// Generates the hand-built delta-v2 golden fixtures in <c>test/fixtures/delta-v2/</c>. They are the cross-language
/// contract between the C# and Rust readers, so they are checked in; <see cref="FixturesAreUpToDate"/> fails when
/// the generator and the checked-in files disagree, and <see cref="RegenerateFixtures"/> (explicit) rewrites them.
/// See the README.md in that directory for the file list and formats.
/// </summary>
public class DeltaV2Fixtures
{
    public static string FixtureDir => PathHelper.GetFixture("delta-v2");

    [Fact(Explicit = true)]
    public void RegenerateFixtures()
    {
        Directory.CreateDirectory(FixtureDir);
        foreach (var kv in Generate()) {
            File.WriteAllBytes(Path.Combine(FixtureDir, kv.Key), kv.Value);
        }
    }

    [Fact]
    public void FixturesAreUpToDate()
    {
        var generated = Generate();
        foreach (var kv in generated) {
            var path = Path.Combine(FixtureDir, kv.Key);
            Assert.True(File.Exists(path), $"fixture missing: {kv.Key} (run RegenerateFixtures)");
            var actual = File.ReadAllBytes(path);
            if (kv.Key.EndsWith(".json") || kv.Key.EndsWith(".md")) {
                Assert.True(NormalizeText(actual) == NormalizeText(kv.Value), $"fixture out of date: {kv.Key} (run RegenerateFixtures)");
            } else {
                Assert.True(actual.AsSpan().SequenceEqual(kv.Value), $"fixture out of date: {kv.Key} (run RegenerateFixtures)");
            }
        }
        foreach (var file in Directory.GetFiles(FixtureDir, "index_*.bin")) {
            Assert.True(generated.ContainsKey(Path.GetFileName(file)), $"unexpected fixture not produced by the generator: {file}");
        }
    }

    private static string NormalizeText(byte[] bytes) => Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").TrimEnd('\n');

    public static Dictionary<string, byte[]> Generate()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        files["index_sample.bin"] = BuildSample().BuildTrailer();
        files["index_unicode_fold.bin"] = BuildUnicodeFold().BuildTrailer();
        foreach (var kv in BuildInvalid()) files["index_invalid_" + kv.Key + ".bin"] = kv.Value;
        files["index_unknown_cdc_algo.bin"] = BuildUnknownCdcAlgo();
        files["plan_vectors.json"] = Text(BuildPlanVectors());
        files["cdc_vectors.json"] = Text(BuildCdcVectors());
        files["feed_null_pointer.json"] = Text(BuildFeedNullPointer());
        return files;
    }

    private static byte[] Text(JsonNode node)
    {
        var options = new JsonSerializerOptions {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var json = node.ToJsonString(options);
        return Encoding.UTF8.GetBytes(json.Replace("\r\n", "\n") + "\n");
    }

    private static byte[] Bytes(ulong seed, int length) => TestIndexBuilder.DeterministicBytes(seed, length);

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    // ---------------------------------------------------------------------------------------------------------------
    // index_sample.bin: every record kind and disposition, a zero-length file, a non-ASCII path, stored and deflate
    // ---------------------------------------------------------------------------------------------------------------

    internal static TestIndexBuilder BuildSample()
    {
        var b = new TestIndexBuilder();
        b.AddFile("Squirrel.exe", Bytes(11, 200_000), ChunkFileDisposition.Updater, chunkLengths: new[] { 70_000, 90_000, 40_000 });
        b.AddFile("MyApp_ExecutionStub.exe", Bytes(12, 30_000), ChunkFileDisposition.Stub, deflate: false);
        b.AddFile("MyApp.exe", Bytes(13, 150_000), chunkLengths: new[] { 100_000, 50_000 });
        b.AddFile("data/empty.txt", Array.Empty<byte>());
        b.AddSymlink("data/link.txt", "../MyApp.exe");
        b.AddEmptyDir("plugins");
        b.AddFile("sq.version", Bytes(14, 1234));
        b.AddFile("Übersicht/ü.txt", Bytes(15, 5000), deflate: false);
        b.Layout();
        return b;
    }

    internal static TestIndexBuilder BuildUnicodeFold()
    {
        // pairs that fold together under Unicode case folding but NOT under the ASCII-only fold the index uses:
        // U+017F LATIN SMALL LETTER LONG S / s, U+212A KELVIN SIGN / k, U+0130 LATIN CAPITAL LETTER I WITH DOT ABOVE / i
        var b = new TestIndexBuilder();
        b.AddFile("\u017F.txt", Bytes(21, 100), deflate: false);
        b.AddFile("s.txt", Bytes(22, 100), deflate: false);
        b.AddFile("\u212A.txt", Bytes(23, 100), deflate: false);
        b.AddFile("k.txt", Bytes(24, 100), deflate: false);
        b.AddFile("\u0130.txt", Bytes(25, 100), deflate: false);
        b.AddFile("i.txt", Bytes(26, 100), deflate: false);
        b.Layout();
        return b;
    }

    private static byte[] BuildUnknownCdcAlgo()
    {
        var b = BuildSample();
        b.CdcAlgo = 77;
        b.CdcMin = 4096;
        b.CdcAvg = 8192;
        b.CdcMax = 16384;
        return b.BuildTrailer();
    }

    private static TestIndexBuilder Small(params string[] paths)
    {
        var b = new TestIndexBuilder();
        ulong seed = 100;
        foreach (var p in paths) b.AddFile(p, Bytes(seed++, 3000), deflate: false);
        b.Layout();
        return b;
    }

    public static Dictionary<string, byte[]> BuildInvalid()
    {
        var r = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var sample = BuildSample().BuildTrailer();

        r["truncated"] = sample.AsSpan(0, sample.Length - 10).ToArray();

        var bytes = (byte[]) sample.Clone();
        bytes[0] = (byte) 'X';
        r["bad_magic"] = bytes;

        bytes = (byte[]) sample.Clone();
        BitConverter.GetBytes((ushort) 2).CopyTo(bytes, 4);
        r["version"] = bytes;

        bytes = (byte[]) sample.Clone();
        bytes[6] = 2;
        r["hash_algo"] = bytes;

        // file_count above the 1,000,000 cap (the buffer is tiny, so this must fail on the cap, not on an allocation)
        bytes = (byte[]) sample.Clone();
        BitConverter.GetBytes(2_000_000u).CopyTo(bytes, 24);
        r["count_overflow"] = bytes;

        // file_count off by one: A no longer fits the buffer
        bytes = (byte[]) sample.Clone();
        BitConverter.GetBytes(BitConverter.ToUInt32(sample, 24) + 1).CopyTo(bytes, 24);
        r["count_mismatch"] = bytes;

        // chunk ownership out of order: MyApp.exe (record 0) claims first_chunk = 1
        bytes = (byte[]) sample.Clone();
        BitConverter.GetBytes(1u).CopyTo(bytes, TestIndexBuilder.FileRecordOffset(bytes, 0) + 16);
        r["noncontiguous_chunks"] = bytes;

        // file length != sum of its chunk lengths
        bytes = (byte[]) sample.Clone();
        BitConverter.GetBytes(150_001UL).CopyTo(bytes, TestIndexBuilder.FileRecordOffset(bytes, 0) + 8);
        r["length_sum"] = bytes;

        // a zero-length chunk record
        bytes = (byte[]) sample.Clone();
        BitConverter.GetBytes(0u).CopyTo(bytes, TestIndexBuilder.ChunkRecordOffset(bytes, 0));
        r["zero_chunk"] = bytes;

        // a chunk longer than 16 MiB
        bytes = (byte[]) sample.Clone();
        BitConverter.GetBytes((16u * 1024 * 1024) + 1).CopyTo(bytes, TestIndexBuilder.ChunkRecordOffset(bytes, 0));
        r["chunk_too_large"] = bytes;

        var b = Small("b.txt", "a.txt");
        b.Sort = false;
        r["unsorted"] = b.BuildTrailer();

        b = Small("a.txt", "a.txt");
        r["duplicate_path"] = b.BuildTrailer();

        r["fold_duplicate"] = Small("Readme.txt", "readme.txt").BuildTrailer();
        r["bad_path"] = Small("ok.txt", "bad:name.txt").BuildTrailer();
        r["trailing_space"] = Small("dir /file.txt").BuildTrailer();
        r["dotdot"] = Small("../escape.txt").BuildTrailer();
        r["dot_segment"] = Small("a/./b.txt").BuildTrailer();
        r["leading_slash"] = Small("/abs.txt").BuildTrailer();
        r["empty_segment"] = Small("a//b.txt").BuildTrailer();
        r["reserved_name"] = Small("data/CON.txt").BuildTrailer();
        r["reserved_name_com"] = Small("com1").BuildTrailer();
        r["sq_chunks_path"] = Small("SQ.chunks").BuildTrailer();
        r["dir_prefix"] = Small("a/b", "a/b-x", "a/b/c").BuildTrailer();
        r["nested_squirrel"] = Small("plugins/Squirrel.exe").BuildTrailer();
        r["nested_stub"] = Small("tools/Foo_ExecutionStub.exe").BuildTrailer();
        r["stub_not_top_level_disposition"] = WithDisposition("tools/Foo_ExecutionStub.exe", ChunkFileDisposition.Stub).BuildTrailer();
        r["stub_wrong_suffix"] = WithDisposition("Foo_executionstub.exe", ChunkFileDisposition.Stub).BuildTrailer();
        r["updater_wrong_path"] = WithDisposition("tools/Squirrel.exe", ChunkFileDisposition.Updater).BuildTrailer();
        r["updater_wrong_name"] = WithDisposition("Updater.exe", ChunkFileDisposition.Updater).BuildTrailer();

        b = Small("MyApp.exe");
        b.AddSymlink("data/link", "../../outside.txt");
        b.Layout();
        r["symlink_escape"] = b.BuildTrailer();

        b = Small("MyApp.exe");
        b.AddSymlink("data/link", "/etc/passwd");
        b.Layout();
        r["symlink_absolute"] = b.BuildTrailer();

        b = Small("MyApp.exe");
        b.AddSymlink("data/link", "C:/Windows/notepad.exe");
        b.Layout();
        r["symlink_drive"] = b.BuildTrailer();

        b = Small("MyApp.exe");
        b.AddSymlink("data/link", "bad|target");
        b.Layout();
        r["symlink_bad_char"] = b.BuildTrailer();

        b = Small("x.txt");
        b.AddEmptyDir("dir");
        b.AddFile("dir/inner.txt", Bytes(5, 10), deflate: false);
        b.Layout();
        r["emptydir_not_empty"] = b.BuildTrailer();

        b = Small("x.txt");
        b.AddEmptyDir("dir").ChunkCountOverride = 1;
        b.Layout();
        r["emptydir_with_chunks"] = b.BuildTrailer();

        b = Small("x.txt");
        b.Entries[0].Kind = (ChunkFileKind) 3;
        r["bad_kind"] = b.BuildTrailer();

        b = Small("x.txt");
        b.Entries[0].Disposition = (ChunkFileDisposition) 3;
        r["bad_disposition"] = b.BuildTrailer();

        b = Small("x.txt");
        b.Entries[0].TargetLengthOverride = 5;
        r["file_with_target"] = b.BuildTrailer();

        // Section B problems
        bytes = (byte[]) sample.Clone();
        int bOff = TestIndexBuilder.SectionALength(bytes);
        bytes[bOff + 3] = (byte) 'X';
        r["b_magic"] = bytes;

        b = BuildSample();
        b.BFileCountOverride = 3;
        r["b_counts"] = b.BuildTrailer();

        b = BuildSample();
        b.Ordered()[0].Chunks[1].Offset += 1; // MyApp.exe's second chunk no longer follows the first
        r["b_noncontiguous"] = b.BuildTrailer();

        b = BuildSample();
        b.Ordered()[0].CompressedSize += 1; // chunk ranges no longer cover the entry data
        r["b_entry_size"] = b.BuildTrailer();

        b = BuildSample();
        b.Ordered()[0].Method = 1;
        r["b_method"] = b.BuildTrailer();

        b = BuildSample();
        var stub = b.Ordered()[1];
        stub.Chunks[0].TransportLength = stub.Chunks[0].Length - 1; // stored chunk range must equal the plaintext range
        r["b_stored_mismatch"] = b.BuildTrailer();

        b = BuildSample();
        b.Ordered()[3].DataOffset = 5; // zero-length file must have an all-zero transport record
        r["b_empty_file_nonzero"] = b.BuildTrailer();

        // trailer has extra bytes after Section B
        bytes = new byte[sample.Length + 1];
        sample.CopyTo(bytes, 0);
        r["trailing_bytes"] = bytes;

        // Section A alone (no transport map) is not a valid trailer
        r["no_section_b"] = TestIndexBuilder.SectionAOf(sample);

        return r;
    }

    private static TestIndexBuilder WithDisposition(string path, ChunkFileDisposition disposition)
    {
        var b = new TestIndexBuilder();
        b.AddFile(path, Bytes(50, 3000), disposition, deflate: false);
        b.Layout();
        return b;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // plan_vectors.json
    // ---------------------------------------------------------------------------------------------------------------

    private sealed class PlanScenario
    {
        public string Name = "";
        public string Description = "";
        public TestIndexBuilder Remote = new();
        public TestIndexBuilder? Local;
        public List<string> UnusableLocal = new();
        public List<byte[]> Blob = new();
        public long AssetSize;
        public Action<ChunkPlan, List<RangeRequest>>? Check;
    }

    private static JsonNode BuildPlanVectors()
    {
        var scenarios = new List<PlanScenario>();

        // 1. identical trees: everything is a whole-file copy
        {
            var s = new PlanScenario { Name = "identical", Description = "remote == local: every file is copied whole, nothing is fetched" };
            s.Remote.AddFile("app.exe", Bytes(1, 300_000));
            s.Remote.AddFile("lib.dll", Bytes(2, 120_000), deflate: false);
            s.AssetSize = s.Remote.Layout() + 500;
            s.Local = new TestIndexBuilder();
            s.Local.AddFile("app.exe", Bytes(1, 300_000));
            s.Local.AddFile("lib.dll", Bytes(2, 120_000), deflate: false);
            s.Check = (p, r) => {
                Assert.Empty(p.Needed);
                Assert.Equal(2, p.WholeFileCopies);
                Assert.Empty(r);
            };
            scenarios.Add(s);
        }

        // 2. one chunk of one file modified; the file's other chunks come from the local copy
        {
            var s = new PlanScenario {
                Name = "modified_chunk", Description = "app.exe differs in its third chunk only; the rest of the tree is unchanged",
            };
            var v1 = Bytes(1, 400_000);
            var v2 = (byte[]) v1.Clone();
            v2[150_000] ^= 0xFF;
            s.Remote.AddFile("app.exe", v2, chunkLengths: new[] { 70_000, 70_000, 70_000, 70_000, 120_000 });
            s.Remote.AddFile("lib.dll", Bytes(2, 120_000));
            s.AssetSize = s.Remote.Layout() + 500;
            s.Local = new TestIndexBuilder();
            s.Local.AddFile("app.exe", v1, chunkLengths: new[] { 70_000, 70_000, 70_000, 70_000, 120_000 });
            s.Local.AddFile("lib.dll", Bytes(2, 120_000));
            s.Check = (p, r) => {
                Assert.Single(p.Needed);
                Assert.Equal(2, p.Needed[0].ChunkIndex);
                Assert.Single(r);
            };
            scenarios.Add(s);
        }

        // 3. added, renamed and deleted files; a renamed file is a whole-file copy, a new file is fetched entirely
        {
            var s = new PlanScenario {
                Name = "add_rename_delete",
                Description = "new.bin is new (fetched), moved.bin is a rename of old.bin (copied whole), gone.bin was deleted",
            };
            s.Remote.AddFile("app.exe", Bytes(1, 200_000));
            s.Remote.AddFile("moved.bin", Bytes(3, 90_000));
            s.Remote.AddFile("new.bin", Bytes(4, 140_000), chunkLengths: new[] { 65_536, 65_536, 8_928 });
            s.AssetSize = s.Remote.Layout() + 500;
            s.Local = new TestIndexBuilder();
            s.Local.AddFile("app.exe", Bytes(1, 200_000));
            s.Local.AddFile("gone.bin", Bytes(5, 50_000));
            s.Local.AddFile("old.bin", Bytes(3, 90_000));
            s.Check = (p, r) => {
                Assert.Equal(3, p.Needed.Count);
                Assert.All(p.Needed, c => Assert.Equal(2, c.FileIndex));
                Assert.Equal(2, p.WholeFileCopies);
                Assert.Single(r); // contiguous chunks of one deflate entry coalesce into one request
            };
            scenarios.Add(s);
        }

        // 4. chunks already in the blob are not fetched again (resume)
        {
            var s = new PlanScenario { Name = "blob_resume", Description = "no local index; two of new.bin's chunks are already in the staged blob" };
            s.Remote.AddFile("new.bin", Bytes(4, 140_000), chunkLengths: new[] { 65_536, 65_536, 8_928 });
            s.AssetSize = 100_000_000;
            var remoteFile = s.Remote.Entries[0];
            s.Blob.Add(remoteFile.Chunks[0].Sha256);
            s.Blob.Add(remoteFile.Chunks[2].Sha256);
            s.Remote.Layout();
            s.Check = (p, r) => {
                Assert.Single(p.Needed);
                Assert.Equal(1, p.Needed[0].ChunkIndex);
            };
            scenarios.Add(s);
        }

        // 5. a local file that fails its sanity check is not a source, even though its index entry matches
        {
            var s = new PlanScenario {
                Name = "local_unusable", Description = "lib.dll's local copy is declared by the local index but is not usable (wrong length on disk)",
            };
            s.Remote.AddFile("app.exe", Bytes(1, 200_000));
            s.Remote.AddFile("lib.dll", Bytes(2, 120_000), chunkLengths: new[] { 60_000, 60_000 });
            s.AssetSize = s.Remote.Layout() + 500;
            s.Local = new TestIndexBuilder();
            s.Local.AddFile("app.exe", Bytes(1, 200_000));
            s.Local.AddFile("lib.dll", Bytes(2, 120_000), chunkLengths: new[] { 60_000, 60_000 });
            s.UnusableLocal.Add("lib.dll");
            s.Check = (p, r) => {
                Assert.Equal(2, p.Needed.Count);
                Assert.Equal(1, p.WholeFileCopies);
            };
            scenarios.Add(s);
        }

        // 6. the same chunk hash in two remote files is fetched once; the same chunk in a local file satisfies both
        {
            var s = new PlanScenario {
                Name = "dedup_shared_chunk",
                Description = "a.bin and b.bin share chunk content; shared chunks are needed once; a local chunk satisfies every remote occurrence",
            };
            var shared = Bytes(7, 65_536);
            var a = shared.Concat(Bytes(8, 20_000)).ToArray();
            var bb = Bytes(9, 30_000).Concat(shared).Concat(Bytes(10, 65_536)).ToArray();
            s.Remote.AddFile("a.bin", a, chunkLengths: new[] { 65_536, 20_000 });
            s.Remote.AddFile("b.bin", bb, chunkLengths: new[] { 30_000, 65_536, 65_536 });
            s.AssetSize = s.Remote.Layout() + 500;
            s.Local = new TestIndexBuilder();
            s.Local.AddFile("c.bin", Bytes(10, 65_536));
            s.Check = (p, r) => {
                // a[0]=shared, a[1], b[0] needed; b[1]=shared deduped; b[2] from local c.bin
                Assert.Equal(3, p.Needed.Count);
                Assert.Equal(new[] { 0, 1, 2 }, p.Needed.Select(c => c.ChunkIndex).ToArray());
            };
            scenarios.Add(s);
        }

        // 7. nothing local, nothing staged, and the package is mostly chunks: transport ceiling → classic path
        {
            var s = new PlanScenario {
                Name = "ceiling_fallback", Description = "no local index: every chunk is needed and the needed bytes are >= 90 % of the package",
            };
            s.Remote.AddFile("app.exe", Bytes(1, 500_000), deflate: false);
            s.Remote.AddFile("lib.dll", Bytes(2, 500_000), deflate: false);
            long end = s.Remote.Layout();
            s.AssetSize = end + 100; // needed = 1,000,000 of ~1,001,200 bytes
            s.Check = (p, r) => Assert.True(p.ExceedsTransportCeiling(s.AssetSize));
            scenarios.Add(s);
        }

        // 8. just under the ceiling
        {
            var s = new PlanScenario { Name = "ceiling_not_reached", Description = "needed bytes are just below 90 % of the package size" };
            s.Remote.AddFile("app.exe", Bytes(1, 900_000), deflate: false);
            s.Remote.Layout();
            s.AssetSize = 1_000_001; // 900,000 * 10 < 1,000,001 * 9
            s.Check = (p, r) => Assert.False(p.ExceedsTransportCeiling(s.AssetSize));
            scenarios.Add(s);
        }

        // 9. coalescing: small gaps merge, a gap above 32 KiB splits, the 8 MiB cap splits, a lone >8 MiB chunk is scheduled alone.
        // Requests are scheduled in pure offset order and merge across file boundaries, so the three groups are separated by
        // files that are fully local (their chunks are never needed and leave a gap larger than 32 KiB).
        {
            var s = new PlanScenario {
                Name = "coalesce", Description = "gap <= 32 KiB merges, a larger gap splits, requests never exceed 8 MiB unless a single chunk does",
            };
            var local = new TestIndexBuilder();
            s.Remote.AddFile("a_big.bin", Bytes(1, 9 * 1024 * 1024), deflate: false, chunkLengths: new[] { 9 * 1024 * 1024 });
            s.Remote.AddFile("b_sep.bin", Bytes(4, 100_000), deflate: false);
            local.AddFile("b_sep.bin", Bytes(4, 100_000), deflate: false);
            // a stored file of nine 1 MiB chunks: the first eight fit one 8 MiB request, the ninth starts a new one
            s.Remote.AddFile("c_nine.bin", Bytes(2, 9 * 1024 * 1024), deflate: false, chunkLengths: Enumerable.Repeat(1024 * 1024, 9).ToArray());
            s.Remote.AddFile("d_sep.bin", Bytes(5, 100_000), deflate: false);
            local.AddFile("d_sep.bin", Bytes(5, 100_000), deflate: false);
            // a deflate file whose chunks are all local except four: 0 and 2 are separated by one local chunk (≈ 20 KB of transport
            // → merge), 5 and 7 by one local chunk whose transport size is pushed above 32 KiB (→ split)
            var content = Bytes(3, 8 * 50_000);
            var remoteGap = s.Remote.AddFile("e_gap.bin", content, chunkLengths: Enumerable.Repeat(50_000, 8).ToArray());
            local.AddFile("e_gap.bin", content, chunkLengths: Enumerable.Repeat(50_000, 8).ToArray());
            foreach (var i in new[] { 0, 2, 5, 7 }) {
                remoteGap.Chunks[i].Sha256 = SHA256.HashData(Bytes((ulong) (900 + i), 50_000));
            }
            remoteGap.Chunks[6].TransportLength = 40_000;
            remoteGap.Sha256 = SHA256.HashData(Bytes(999, 10));
            s.AssetSize = s.Remote.Layout() + 500;
            s.Local = local;
            s.Check = (p, r) => {
                Assert.Equal(1 + 9 + 4, p.Needed.Count);
                Assert.Equal(2, p.WholeFileCopies);
                // in offset order: a_big.bin (one lone request > 8 MiB), c_nine.bin (8 + 1), e_gap.bin ({0,2} merged, {5}, {7})
                Assert.Equal(1 + 2 + 3, r.Count);
                Assert.Equal(9L * 1024 * 1024, r[0].Length);
                Assert.Single(r[0].Chunks);
                Assert.Equal(8, r[1].Chunks.Count);
                Assert.Equal(8L * 1024 * 1024, r[1].Length);
                Assert.Single(r[2].Chunks);
                Assert.Equal(2, r[3].Chunks.Count);
                Assert.Single(r[4].Chunks);
                Assert.Single(r[5].Chunks);
            };
            scenarios.Add(s);
        }

        // 10. a stored remote entry: transport ranges are the plaintext ranges
        {
            var s = new PlanScenario {
                Name = "stored_entry", Description = "a stored (method 0) entry: the needed ranges are the plaintext ranges and are contiguous",
            };
            s.Remote.AddFile("data.bin", Bytes(1, 100_000), deflate: false, chunkLengths: new[] { 40_000, 60_000 });
            s.AssetSize = s.Remote.Layout() + 500;
            s.Check = (p, r) => {
                Assert.Equal(2, p.Needed.Count);
                Assert.Equal(100_000, p.NeededCompressedBytes);
                Assert.Single(r);
                Assert.Equal(100_000, r[0].Length);
            };
            scenarios.Add(s);
        }

        // 11. the local index is present but has nothing in common
        {
            var s = new PlanScenario {
                Name = "nothing_in_common", Description = "local index present but shares no file or chunk hashes with the remote",
            };
            s.Remote.AddFile("app.exe", Bytes(1, 200_000), chunkLengths: new[] { 100_000, 100_000 });
            s.AssetSize = 10_000_000;
            s.Remote.Layout();
            s.Local = new TestIndexBuilder();
            s.Local.AddFile("app.exe", Bytes(2, 200_000), chunkLengths: new[] { 100_000, 100_000 });
            s.Check = (p, r) => Assert.Equal(2, p.Needed.Count);
            scenarios.Add(s);
        }

        var vectors = new JsonArray();
        foreach (var s in scenarios) {
            var remoteBytes = s.Remote.BuildTrailer();
            var remote = ChunkIndex.ParseTrailer(remoteBytes, s.AssetSize);
            byte[]? localBytes = s.Local?.BuildSectionA();
            var local = localBytes != null ? ChunkIndex.ParseLocal(localBytes) : null;
            var blob = new HashSet<Hash256>(s.Blob.Select(h => Hash256.FromBytes(h)));
            var plan = ChunkPlan.Create(remote, local, i => !s.UnusableLocal.Contains(local!.GetPath(i)), blob.Contains);
            var ranges = ChunkPlan.Coalesce(plan.Needed);
            s.Check?.Invoke(plan, ranges);

            var node = new JsonObject {
                ["name"] = s.Name,
                ["description"] = s.Description,
                ["asset_size"] = s.AssetSize,
                ["remote_trailer_hex"] = Hex(remoteBytes),
                ["local_index_hex"] = localBytes != null ? Hex(localBytes) : null,
                ["local_unusable_paths"] = new JsonArray(s.UnusableLocal.Select(p => (JsonNode) p).ToArray()),
                ["blob_hashes"] = new JsonArray(s.Blob.Select(h => (JsonNode) Hex(h)).ToArray()),
                ["expected"] = new JsonObject {
                    ["needed"] = new JsonArray(plan.Needed.Select(c => (JsonNode) new JsonObject {
                        ["chunk"] = c.ChunkIndex,
                        ["file"] = c.FileIndex,
                        ["sha256"] = c.Hash.ToHex(),
                        ["method"] = c.Method,
                        ["offset"] = c.Offset,
                        ["length"] = c.Length,
                        ["plain_length"] = c.PlainLength,
                    }).ToArray()),
                    ["needed_compressed_bytes"] = plan.NeededCompressedBytes,
                    ["needed_plaintext_bytes"] = plan.NeededPlaintextBytes,
                    ["remote_file_bytes"] = plan.RemoteFileBytes,
                    ["whole_file_copies"] = plan.WholeFileCopies,
                    ["classic_fallback"] = plan.ExceedsTransportCeiling(s.AssetSize),
                    ["ranges"] = new JsonArray(ranges.Select(r => (JsonNode) new JsonObject {
                        ["offset"] = r.Offset,
                        ["length"] = r.Length,
                        ["chunks"] = new JsonArray(r.Chunks.Select(c => (JsonNode) c.ChunkIndex).ToArray()),
                    }).ToArray()),
                },
            };
            vectors.Add(node);
        }

        return new JsonObject {
            ["format"] = "Velopack delta-v2 plan vectors (see README.md)",
            ["constants"] = new JsonObject {
                ["max_request_length"] = ChunkPlan.MaxRequestLength,
                ["max_gap"] = ChunkPlan.MaxGap,
                ["transport_ceiling"] = ChunkPlan.TransportCeiling,
                ["planning_hash_threshold"] = ChunkPlan.PlanningHashThreshold,
            },
            ["vectors"] = vectors,
        };
    }

    // ---------------------------------------------------------------------------------------------------------------
    // cdc_vectors.json (§3 goldens, verified against a reference implementation while generating)
    // ---------------------------------------------------------------------------------------------------------------

    private const int CdcMin = 16384, CdcAvg = 65536, CdcMax = 262144;
    private const ulong MaskS = 0xFFFFC00000000000UL, MaskL = 0xFFFC000000000000UL;
    private const ulong GearSeed = 0x56454C4F5041434BUL;

    public static ulong[] BuildGearTable()
    {
        var gear = new ulong[256];
        ulong state = GearSeed;
        for (int i = 0; i < 256; i++) gear[i] = TestIndexBuilder.SplitMix64Next(ref state);
        return gear;
    }

    /// <summary>The reference chunker of §3 (whole-buffer form): returns the chunk lengths.</summary>
    public static List<int> ReferenceChunk(byte[] data, ulong[] gear)
    {
        var lengths = new List<int>();
        int n = data.Length, pos = 0;
        while (pos < n) {
            int remaining = n - pos;
            if (remaining <= CdcMin) {
                lengths.Add(remaining);
                break;
            }
            int end = Math.Min(remaining, CdcMax);
            int normal = Math.Min(remaining, CdcAvg);
            ulong h = 0;
            int i = CdcMin, cut = end;
            bool found = false;
            for (; i < normal; i++) {
                h = unchecked((h << 1) + gear[data[pos + i]]);
                if ((h & MaskS) == 0) {
                    cut = i + 1;
                    found = true;
                    break;
                }
            }
            if (!found) {
                for (; i < end; i++) {
                    h = unchecked((h << 1) + gear[data[pos + i]]);
                    if ((h & MaskL) == 0) {
                        cut = i + 1;
                        break;
                    }
                }
            }
            lengths.Add(cut);
            pos += cut;
        }
        return lengths;
    }

    private static string LengthsSha(List<int> lengths)
    {
        var bytes = new byte[lengths.Count * 4];
        for (int i = 0; i < lengths.Count; i++) BitConverter.GetBytes(lengths[i]).CopyTo(bytes, i * 4);
        return Hex(SHA256.HashData(bytes));
    }

    private static JsonNode BuildCdcVectors()
    {
        var gear = BuildGearTable();
        Assert.Equal(0xD176D219BDB4EC54UL, gear[0]);
        Assert.Equal(0xF388586F617F38B9UL, gear[1]);
        Assert.Equal(0xF823673E0DBA937CUL, gear[255]);
        var tableBytes = new byte[256 * 8];
        for (int i = 0; i < 256; i++) BitConverter.GetBytes(gear[i]).CopyTo(tableBytes, i * 8);
        var tableSha = Hex(SHA256.HashData(tableBytes));
        Assert.Equal("D82385ADFED88D11F3DD3DDB4028CFD4E1ED8ACB8C6C387E6EA7759DA39607CD", tableSha);

        var v1 = Bytes(1, 4 * 1024 * 1024);
        var v1DataSha = Hex(SHA256.HashData(v1));
        Assert.Equal("AD6BC3DEF4BC89ADB670062BB9B9F48CD0AA748258F31A304D1DF877D46CC6FE", v1DataSha);
        var l1 = ReferenceChunk(v1, gear);
        Assert.Equal(60, l1.Count);
        Assert.Equal(new[] { 78906, 110685, 19000, 82052, 85964, 91242, 21314, 68305 }, l1.Take(8).ToArray());
        Assert.Equal(66477, l1[^1]);
        Assert.Equal(18387, l1.Min());
        Assert.Equal(233193, l1.Max());
        Assert.Equal("9E0F146E99494451B545879DF7438D45DF2A5AFC4DCE5E97DE564BD444F88FA3", LengthsSha(l1));
        var chunk0Sha = Hex(SHA256.HashData(v1.AsSpan(0, l1[0])));
        Assert.Equal("49C947FC97C644CF031BBD1BB79ED8D43E9E1E3260C55D428C9E7993E49D198F", chunk0Sha);

        const int insertAt = 1024 * 1024, insertLen = 1000;
        var v2 = v1.Take(insertAt).Concat(Bytes(7, insertLen)).Concat(v1.Skip(insertAt)).ToArray();
        var l2 = ReferenceChunk(v2, gear);
        Assert.Equal(60, l2.Count);
        Assert.Equal("302F9666750AB68CCA5C6BD933584BC27AF6DA333A08D08687BD2F0569B0844E", LengthsSha(l2));
        var b1 = new HashSet<long>();
        long acc = 0;
        foreach (var l in l1) b1.Add(acc += l);
        acc = 0;
        foreach (var l in l2) {
            acc += l;
            if (acc > insertAt + CdcMax + insertLen) Assert.Contains(acc - insertLen, b1);
        }

        var zeros = ReferenceChunk(new byte[1024 * 1024], gear);
        Assert.Equal(new[] { 262144, 262144, 262144, 262144 }, zeros.ToArray());
        Assert.Empty(ReferenceChunk(Array.Empty<byte>(), gear));
        Assert.Equal(new[] { 16384 }, ReferenceChunk(Bytes(3, 16384), gear).ToArray());
        Assert.Equal(new[] { 16385 }, ReferenceChunk(Bytes(3, 16385), gear).ToArray());

        JsonArray Ints(IEnumerable<int> xs) => new JsonArray(xs.Select(x => (JsonNode) x).ToArray());

        return new JsonObject {
            ["format"] = "Velopack delta-v2 FastCDC-gear-v1 golden vectors (see README.md)",
            ["gear"] = new JsonObject {
                ["splitmix64_seed"] = GearSeed.ToString("X16"),
                ["gear_0"] = gear[0].ToString("X16"),
                ["gear_1"] = gear[1].ToString("X16"),
                ["gear_255"] = gear[255].ToString("X16"),
                ["table_sha256_le"] = tableSha,
            },
            ["params"] = new JsonObject {
                ["min"] = CdcMin,
                ["avg"] = CdcAvg,
                ["max"] = CdcMax,
                ["mask_s"] = MaskS.ToString("X16"),
                ["mask_l"] = MaskL.ToString("X16"),
            },
            ["data_generator"] = "SplitMix64 stream from the given seed, each output as 8 little-endian bytes, truncated to size",
            ["vectors"] = new JsonArray {
                new JsonObject {
                    ["name"] = "vector1",
                    ["seed"] = 1,
                    ["size"] = v1.Length,
                    ["data_sha256"] = v1DataSha,
                    ["chunk_count"] = l1.Count,
                    ["lengths"] = Ints(l1),
                    ["lengths_sha256_u32le"] = LengthsSha(l1),
                    ["chunk0_sha256"] = chunk0Sha,
                },
                new JsonObject {
                    ["name"] = "vector2",
                    ["base"] = "vector1",
                    ["insert_seed"] = 7,
                    ["insert_offset"] = insertAt,
                    ["insert_length"] = insertLen,
                    ["size"] = v2.Length,
                    ["data_sha256"] = Hex(SHA256.HashData(v2)),
                    ["chunk_count"] = l2.Count,
                    ["lengths"] = Ints(l2),
                    ["lengths_sha256_u32le"] = LengthsSha(l2),
                },
                new JsonObject { ["name"] = "zeros", ["size"] = 1024 * 1024, ["lengths"] = Ints(zeros) },
                new JsonObject { ["name"] = "empty", ["size"] = 0, ["lengths"] = new JsonArray() },
                new JsonObject { ["name"] = "min", ["seed"] = 3, ["size"] = 16384, ["lengths"] = Ints(new[] { 16384 }) },
                new JsonObject { ["name"] = "min_plus_one", ["seed"] = 3, ["size"] = 16385, ["lengths"] = Ints(new[] { 16385 }) },
            },
        };
    }

    // ---------------------------------------------------------------------------------------------------------------
    // feed_null_pointer.json: a third-party feed that emits the pointer keys as null must still parse
    // ---------------------------------------------------------------------------------------------------------------

    private static JsonNode BuildFeedNullPointer()
    {
        return new JsonObject {
            ["Assets"] = new JsonArray {
                new JsonObject {
                    ["PackageId"] = "NullPointerApp",
                    ["Version"] = "1.0.0",
                    ["Type"] = "Full",
                    ["FileName"] = "NullPointerApp-1.0.0-full.nupkg",
                    ["SHA1"] = "DA39A3EE5E6B4B0D3255BFEF95601890AFD80709",
                    ["SHA256"] = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                    ["Size"] = 1000,
                    ["ChunkIndexOffset"] = null,
                    ["ChunkIndexLength"] = null,
                    ["ChunkIndexSHA256"] = null,
                },
                new JsonObject {
                    ["PackageId"] = "NullPointerApp",
                    ["Version"] = "1.1.0",
                    ["Type"] = "Full",
                    ["FileName"] = "NullPointerApp-1.1.0-full.nupkg",
                    ["SHA1"] = "DA39A3EE5E6B4B0D3255BFEF95601890AFD80709",
                    ["SHA256"] = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                    ["Size"] = 2000,
                    ["ChunkIndexOffset"] = 1500,
                    ["ChunkIndexLength"] = 400,
                    ["ChunkIndexSHA256"] = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
                },
                new JsonObject {
                    ["PackageId"] = "NullPointerApp",
                    ["Version"] = "0.9.0",
                    ["Type"] = "Full",
                    ["FileName"] = "NullPointerApp-0.9.0-full.nupkg",
                    ["SHA1"] = "DA39A3EE5E6B4B0D3255BFEF95601890AFD80709",
                    ["SHA256"] = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                    ["Size"] = 900,
                },
            },
        };
    }
}
