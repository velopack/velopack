//! Chunk-based ("delta v2") update engine: index format, chunk blob, planning, staging and assembly.
//!
//! The formats and constants here are shared byte-for-byte with the C# library and with vpk;
//! see the delta v2 spec. Only the pack side (CDC + zip writer) lives in vpk.

pub mod assemble;
pub mod blob;
pub mod fetch;
pub mod fetcher;
pub mod index;
pub mod inflate;
pub mod plan;
pub mod staging;

pub use assemble::{assemble, AssembleContext, AssembleError};
pub use blob::ChunkBlob;
pub use fetch::{RangeError, RangeReader};
pub use fetcher::{fetch_index, fetch_ranges, Budget, ErrorClass, FetchError, RetryPolicy};
pub use index::{staged_id, ChunkIndex, ChunkRecord, Disposition, FileKind, FileRecord, IndexError};
pub use inflate::{inflate_chunk, parse_sha256_hex, sha256, sha256_file, sha256_hex, InflateError};
pub use plan::{
    coalesce, exceeds_transport_ceiling, local_file_usable, plan, plan_for_hashes, plan_with, LocalRoots, NeededChunk, Plan, RangeRequest,
};
pub use staging::{find_ready_staged, find_ready_staged_for, gc_staged, gc_staged_for_run, read_ready_marker, StagedLayout, StagedReadyMarker};

/// Magic of Section A (the content map), "VPKA".
pub const A_MAGIC: [u8; 4] = *b"VPKA";
/// Magic of Section B (the transport map), "VPKB".
pub const B_MAGIC: [u8; 4] = *b"VPKB";
/// Magic of a chunk blob record, "VCHK".
pub const BLOB_MAGIC: [u8; 4] = *b"VCHK";
/// The only supported index format version.
pub const FORMAT_VERSION: u16 = 1;
/// The only supported hash algorithm (SHA-256).
pub const HASH_ALGO_SHA256: u8 = 1;
/// FastCDC-gear-v1, the advisory chunking algorithm vpk uses.
pub const CDC_ALGO_FASTCDC_V1: u8 = 1;
/// Advisory CDC parameters as written by vpk.
pub const CDC_MIN: u32 = 16384;
pub const CDC_AVG: u32 = 65536;
pub const CDC_MAX: u32 = 262144;

pub const A_HEADER_LEN: usize = 48;
pub const FILE_RECORD_LEN: usize = 64;
pub const CHUNK_RECORD_LEN: usize = 36;
pub const B_HEADER_LEN: usize = 16;
pub const B_FILE_RECORD_LEN: usize = 24;
pub const B_CHUNK_RECORD_LEN: usize = 12;

pub const MAX_FILES: u64 = 1_000_000;
pub const MAX_CHUNKS: u64 = 4_194_304;
pub const MAX_PATH_BLOB_LEN: u64 = 64 * 1024 * 1024;
pub const MAX_A_LEN: u64 = 256 * 1024 * 1024;
pub const MAX_TRAILER_LEN: u64 = 512 * 1024 * 1024;
pub const MAX_CHUNK_LEN: u32 = 16 * 1024 * 1024;
pub const MAX_PATH_LEN: usize = 1024;
pub const MAX_SEGMENT_LEN: usize = 255;

/// The zip entry name of the trailer (A‖B) inside a `.nupkg`.
pub const TRAILER_ENTRY_NAME: &str = "package/velopack/chunks.idx";
/// The file name of the local content map in the installed `current/` directory.
pub const LOCAL_INDEX_NAME: &str = "sq.chunks";
/// The zip entry name of the local content map inside a `.nupkg`.
pub const LIB_APP_INDEX_NAME: &str = "lib/app/sq.chunks";
/// The updater entry name inside a package (disposition Updater).
pub const UPDATER_ENTRY_NAME: &str = "Squirrel.exe";
/// The installed name of the updater.
pub const UPDATER_INSTALLED_NAME: &str = "Update.exe";
/// The (case-sensitive) suffix of execution stub entries (disposition Stub).
pub const STUB_SUFFIX: &str = "_ExecutionStub.exe";

/// The deflate terminator appended to every chunk before inflating it: an empty final stored block.
pub const INFLATE_TERMINATOR: [u8; 5] = [0x01, 0x00, 0x00, 0xFF, 0xFF];
/// The final block vpk writes after the last chunk of a deflate entry (never fetched).
pub const ENTRY_TERMINATOR: [u8; 2] = [0x03, 0x00];

// Staged layout names (§6.1).
pub const STAGED_DIR_NAME: &str = "staged";
pub const STAGED_ASSET_JSON: &str = "asset.json";
pub const STAGED_INDEX_BIN: &str = "index.bin";
pub const STAGED_CHUNKS_BIN: &str = "chunks.bin";
pub const STAGED_MISSING_BIN: &str = "missing.bin";
pub const STAGED_READY_JSON: &str = "ready.json";
pub const STAGED_CURRENT_DIR: &str = "current";
pub const STAGED_STUBS_DIR: &str = "stubs";
pub const READY_MARKER_FORMAT: u32 = 1;
pub const PARTIAL_SUFFIX: &str = ".vpkpart";

/// Exclusive lock held across the two renames of a swap, and taken non-blocking by recovery.
pub const SWAP_LOCK_NAME: &str = ".velopack_swap_lock";
/// Prefix of the backup directory a swap renames `current` to.
pub const BACKUP_DIR_PREFIX: &str = "current.old-";
/// Prefix of the directory a staged tree is copied into when it lives outside the root.
pub const COPY_DIR_PREFIX: &str = "current.new-";

// Planning and fetch policy (§5).
pub const FETCH_PARALLELISM: usize = 4;
pub const REQUEST_CAP: u64 = 8 * 1024 * 1024;
pub const COALESCE_GAP: u64 = 32 * 1024;
pub const TRANSPORT_CEILING: f64 = 0.9;
/// `TRANSPORT_CEILING` as the exact fraction used for the comparison (`needed * 10 >= size * 9`).
pub const TRANSPORT_CEILING_NUMERATOR: u128 = 9;
pub const TRANSPORT_CEILING_DENOMINATOR: u128 = 10;
pub const PLANNING_HASH_THRESHOLD: u64 = 64 * 1024;
pub const BLOB_FLUSH_INTERVAL: u64 = 16 * 1024 * 1024;
pub const FREE_SPACE_FACTOR: f64 = 1.1;
pub const FREE_SPACE_MARGIN: u64 = 64 * 1024 * 1024;
pub const HEADERS_TIMEOUT_SECS: u64 = 30;
pub const READ_STALL_TIMEOUT_SECS: u64 = 30;
pub const REQUEST_TIMEOUT_SECS: u64 = 300;
/// The slowest throughput a ranged request's whole-request timeout allows for its body (on top of
/// `REQUEST_TIMEOUT_SECS`).
pub const MIN_RANGE_THROUGHPUT_BYTES_PER_SEC: u64 = 16 * 1024;
pub const BACKOFF_BASE_SECS: u64 = 1;
pub const BACKOFF_MAX_SECS: u64 = 30;
pub const RETRY_AFTER_MAX_SECS: u64 = 60;
pub const MAX_ATTEMPTS_PER_REQUEST: u32 = 8;
pub const MAX_HASH_MISMATCHES_PER_REQUEST: u32 = 3;
pub const MAX_SECS_WITHOUT_PROGRESS: u64 = 10 * 60;
pub const MAX_CONSECUTIVE_REFRESHES: u32 = 3;
pub const MAX_MISSING_ROUNDS: u32 = 2;
pub const ASSEMBLE_WORKERS: usize = 4;

// Progress bands of the chunk path in download_updates (§8.4).
pub const PROGRESS_INDEX_END: i16 = 3;
pub const PROGRESS_FETCH_END: i16 = 70;
pub const PROGRESS_ASSEMBLE_END: i16 = 97;
