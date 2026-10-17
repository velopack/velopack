# delta-v2 golden fixtures

Hand-built fixtures for the chunk-based ("delta v2") update format. They are the cross-language contract:
the C# reader (`src/lib-csharp/Chunks`) and the Rust reader (`src/lib-rust/src/chunks`) are both checked
against them, so a change here is a format change.

Every file in this directory except the packed `ChunkTestApp-*` packages and `releases.win.json` is produced
by the generator `test/Velopack.Tests/Chunks/DeltaV2Fixtures.cs`:

- `FixturesAreUpToDate` (always runs) regenerates everything in memory and fails if a checked-in file differs;
- `RegenerateFixtures` (`[Fact(Explicit = true)]`) rewrites the files:
  `dotnet test --project test/Velopack.Tests -- --filter-method "*RegenerateFixtures*" -explicit only`
  (xunit v3 skips explicit tests unless `-explicit on|only` is passed).

The generator is deterministic: all content comes from SplitMix64 streams (`TestIndexBuilder.DeterministicBytes`),
hashes are real SHA-256 values of that content, and transport layouts are computed by `TestIndexBuilder.Layout`.

## Index fixtures (`index_*.bin`)

Every `.bin` file is a complete **trailer** (Section A ‖ Section B, the content of `package/velopack/chunks.idx`).
The local content map (`lib/app/sq.chunks`) is the Section A prefix: its length is
`48 + path_blob_len + 64 * file_count + 36 * chunk_count` from the A header.

| File | Expectation |
|---|---|
| `index_sample.bin` | Parses. 8 records: `MyApp.exe` (AppFile, 2 deflate chunks), `MyApp_ExecutionStub.exe` (Stub, stored), `Squirrel.exe` (Updater, 3 chunks), `data/empty.txt` (zero-length file), `data/link.txt` (Symlink → `../MyApp.exe`), `plugins` (EmptyDir), `sq.version`, `Übersicht/ü.txt` (non-ASCII, stored). Transport ranges are synthetic but consistent (deflate chunks contiguous from the entry's data offset with `compressed_size = Σ + 2`; stored chunks map 1:1). |
| `index_unicode_fold.bin` | Parses. Pairs that are equal under Unicode case folding but not under the ASCII-only fold of the index rules: `ſ.txt`/`s.txt` (U+017F), `K.txt`/`k.txt` (U+212A Kelvin sign), `İ.txt`/`i.txt` (U+0130). |
| `index_unknown_cdc_algo.bin` | Parses. The sample with `cdc_algo = 77` and odd CDC parameters: those header fields are advisory. |
| `index_invalid_*.bin` | Each must be **rejected** by `ParseTrailer`. The suffix names the violation (see the generator for the exact mutation). Some of them (`b_*`, `no_section_b`, `trailing_bytes`) are only invalid as trailers; their Section A prefix may be a valid local map. |

Invalid cases: `truncated`, `bad_magic`, `version`, `hash_algo`, `count_overflow` (file_count above the cap),
`count_mismatch` (A does not fit the buffer), `noncontiguous_chunks`, `length_sum`, `zero_chunk`,
`chunk_too_large`, `unsorted`, `duplicate_path`, `fold_duplicate`, `bad_path`, `trailing_space`, `dotdot`,
`dot_segment`, `leading_slash`, `empty_segment`, `reserved_name`, `reserved_name_com`, `sq_chunks_path`,
`dir_prefix`, `nested_squirrel`, `nested_stub`, `stub_not_top_level_disposition`, `stub_wrong_suffix`,
`updater_wrong_path`, `updater_wrong_name`, `symlink_escape`, `symlink_absolute`, `symlink_drive`,
`symlink_bad_char`, `emptydir_not_empty`, `emptydir_with_chunks`, `bad_kind`, `bad_disposition`,
`file_with_target`, `b_magic`, `b_counts`, `b_noncontiguous`, `b_entry_size`, `b_method`,
`b_stored_mismatch`, `b_empty_file_nonzero`, `trailing_bytes`, `no_section_b`.

## `plan_vectors.json`

Planning (§5.3) and coalescing (§5.4) vectors. Each vector carries the inputs as bytes so no builder is needed:

- `remote_trailer_hex`: the remote trailer (parse with the asset size `asset_size`);
- `local_index_hex`: the local Section A, or `null` for "no local index";
- `local_unusable_paths`: local File records that the usability check rejects (every other local file is usable);
- `blob_hashes`: chunk hashes already present (verified) in the staged blob;
- `expected.needed`: the chunks to fetch **in remote file order, deduplicated by hash**, each with its remote
  chunk index, file index, hash, zip method, transport offset/length and plaintext length;
- `expected.needed_compressed_bytes` / `needed_plaintext_bytes` / `remote_file_bytes` / `whole_file_copies`;
- `expected.classic_fallback`: `needed_compressed_bytes * 10 >= asset_size * 9` (the 90 % transport ceiling);
- `expected.ranges`: the coalesced requests (sorted by offset; merge when gap ≤ 32 KiB and merged length ≤ 8 MiB;
  a request always holds at least one chunk) with the chunk indexes they carry, in order.

## `cdc_vectors.json`

The FastCDC-gear-v1 goldens of the spec (§3): the gear table seed and frozen values, the parameters and masks,
and the vectors. Data is the SplitMix64 stream of `seed`, each output written as 8 little-endian bytes,
truncated to `size`. `lengths_sha256_u32le` is the SHA-256 of the chunk lengths serialised as `u32` LE.
`vector2` is `vector1` with `insert_length` bytes of the `insert_seed` stream inserted at `insert_offset`.
The generator verifies all frozen values against a reference implementation before writing the file.

## `feed_null_pointer.json`

A `releases.{channel}.json` feed whose first asset emits the pointer keys `ChunkIndexOffset`,
`ChunkIndexLength` and `ChunkIndexSHA256` as JSON `null` (as some third-party feed generators do), a second
asset with a real pointer, and a third without the keys. Readers must accept all three (null ⇒ "no pointer").

## Packed packages

`ChunkTestApp-1.0.0-win-full.nupkg`, `ChunkTestApp-1.1.0-win-full.nupkg` and `releases.win.json` are produced by
the vpk chunk-aligned writer (`ChunkIndexBuilder` + `ChunkedZipWriter`), not by this generator. The generator is
`test/Velopack.Packaging.Tests/Chunking/DeltaV2PackageFixtures.cs`: a synthetic tree whose 1.1.0 has a modified large
file (1 KiB inserted), a renamed file, a deleted file and an added file, plus a stub and a `Squirrel.exe` placeholder
(no empty directories, no symlinks). Compressed bytes depend on the runtime's zlib, so regenerate on net8 only:

```
dotnet test --project test/Velopack.Packaging.Tests -f net8.0 -- --filter-method "*RegenerateChunkTestAppFixtures*" -explicit only
```

`ChunkTestAppFixturesMatchGenerator` (always runs) checks the runtime-independent parts: Section A, every entry's
content, the entry order, and the feed's pointer and checksums.
