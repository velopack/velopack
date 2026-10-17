using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Velopack.Chunks
{
    /// <summary>
    /// The append-only chunk blob <c>staged/&lt;id&gt;/chunks.bin</c> (§6.2): a sequence of
    /// <c>"VCHK" ‖ u32 length ‖ u8[32] sha256 ‖ payload</c> records. Opening performs one sequential verifying read:
    /// records whose payload hash matches are indexed (first copy wins), records whose payload does not match are
    /// skipped (the crash/power-loss tail: a flushed header with an unflushed payload), and the first structural
    /// failure ends the scan and truncates the file there. All operations are serialised by an internal lock, so the
    /// fetch workers can share one instance.
    /// </summary>
    internal sealed class ChunkBlob : IDisposable
    {
        public const string FileName = "chunks.bin";
        public const int RecordHeaderSize = 40;
        public const long FlushInterval = 16L * 1024 * 1024;

        private static readonly byte[] Magic = { 0x56, 0x43, 0x48, 0x4B }; // "VCHK"

        private readonly object _lock = new object();
        private readonly FileStream _stream;
        private readonly Dictionary<Hash256, Location> _index = new Dictionary<Hash256, Location>();
        private readonly byte[] _header = new byte[RecordHeaderSize];
        private long _unflushed;
        private bool _disposed;

        private readonly struct Location
        {
            public readonly long Offset;
            public readonly int Length;

            public Location(long offset, int length)
            {
                Offset = offset;
                Length = length;
            }
        }

        private ChunkBlob(FileStream stream)
        {
            _stream = stream;
        }

        /// <summary>
        /// Opens (or creates) a blob file and indexes its verified records. The file is opened with read/write sharing:
        /// writers are serialised by the packages lock, and the assembler only reads it.
        /// </summary>
        public static ChunkBlob Open(string path)
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, 64 * 1024);
            try {
                var blob = new ChunkBlob(stream);
                blob.Scan();
                return blob;
            } catch {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>Number of distinct verified chunks in the blob.</summary>
        public int Count {
            get {
                lock (_lock) return _index.Count;
            }
        }

        /// <summary>Current length of the blob file in bytes.</summary>
        public long Length {
            get {
                lock (_lock) return _stream.Length;
            }
        }

        /// <summary>A snapshot of the verified chunk hashes.</summary>
        public List<Hash256> GetHashes()
        {
            lock (_lock) return new List<Hash256>(_index.Keys);
        }

        public bool Contains(Hash256 hash)
        {
            lock (_lock) return _index.ContainsKey(hash);
        }

        /// <summary>
        /// Reads a chunk's plaintext, re-hashing it. Returns null when the chunk is not in the blob or its payload no
        /// longer matches (in which case it is dropped from the index).
        /// </summary>
        public byte[]? Read(Hash256 hash)
        {
            lock (_lock) {
                ThrowIfDisposed();
                if (!_index.TryGetValue(hash, out var loc)) return null;
                var data = new byte[loc.Length];
                _stream.Seek(loc.Offset, SeekOrigin.Begin);
                if (!ReadExactly(data, 0, data.Length)) {
                    _index.Remove(hash);
                    return null;
                }
                if (ChunkInflate.Sha256(data) != hash) {
                    _index.Remove(hash);
                    return null;
                }
                return data;
            }
        }

        /// <summary>
        /// Appends a verified chunk (the caller has checked that <paramref name="hash"/> is the SHA-256 of the payload).
        /// Always writes a record, even when the hash is already indexed (missing-round semantics, §5.7); the index
        /// then points at the fresh copy. The file is flushed to disk every <see cref="FlushInterval"/> appended bytes.
        /// </summary>
        public void Append(Hash256 hash, byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || data.Length - offset < count) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0 || count > ChunkIndex.MaxChunkLength) throw new ArgumentOutOfRangeException(nameof(count), "A chunk is 1..16 MiB.");

            lock (_lock) {
                ThrowIfDisposed();
                long recordOffset = _stream.Seek(0, SeekOrigin.End);
                Buffer.BlockCopy(Magic, 0, _header, 0, 4);
                LittleEndian.WriteUInt32(_header, 4, (uint) count);
                hash.CopyTo(_header, 8);
                _stream.Write(_header, 0, RecordHeaderSize);
                _stream.Write(data, offset, count);
                _index[hash] = new Location(recordOffset + RecordHeaderSize, count);
                _unflushed += RecordHeaderSize + count;
                if (_unflushed >= FlushInterval) {
                    FlushCore();
                }
            }
        }

        public void Append(Hash256 hash, byte[] data) => Append(hash, data, 0, data.Length);

        /// <summary>Flushes buffered records to disk (<c>Flush(true)</c>).</summary>
        public void Flush()
        {
            lock (_lock) {
                ThrowIfDisposed();
                FlushCore();
            }
        }

        /// <summary>
        /// Flushes and closes the blob, best effort: a failed flush (disk full after a failed append) must not replace the
        /// exception that is unwinding through the caller's <c>using</c>. Unflushed records are not lost silently: the next
        /// open verifies every record and the plan fetches whatever is missing. Callers that need the data on disk call
        /// <see cref="Flush"/> first.
        /// </summary>
        public void Dispose()
        {
            lock (_lock) {
                if (_disposed) return;
                _disposed = true;
                try {
                    FlushCore();
                } catch {
                    // see above
                }
                try {
                    _stream.Dispose();
                } catch {
                    // the handle is closed even when the final buffer flush throws
                }
            }
        }

        private void FlushCore()
        {
            if (_unflushed > 0) {
                _stream.Flush(true);
                _unflushed = 0;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ChunkBlob));
        }

        private void Scan()
        {
            long length = _stream.Length;
            long pos = 0;
            var buffer = new byte[64 * 1024];
            using var sha = SHA256.Create();
            _stream.Seek(0, SeekOrigin.Begin);

            while (pos < length) {
                if (length - pos < RecordHeaderSize) break; // truncated header
                if (!ReadExactly(_header, 0, RecordHeaderSize)) break;
                if (_header[0] != Magic[0] || _header[1] != Magic[1] || _header[2] != Magic[2] || _header[3] != Magic[3]) break;
                uint payloadLength = LittleEndian.ReadUInt32(_header, 4);
                if (payloadLength == 0 || payloadLength > ChunkIndex.MaxChunkLength) break;
                if (length - pos - RecordHeaderSize < payloadLength) break; // truncated payload
                var expected = Hash256.FromBytes(_header, 8);

                sha.Initialize();
                long remaining = payloadLength;
                bool ok = true;
                while (remaining > 0) {
                    int n = _stream.Read(buffer, 0, (int) Math.Min(buffer.Length, remaining));
                    if (n <= 0) {
                        ok = false;
                        break;
                    }
                    sha.TransformBlock(buffer, 0, n, null, 0);
                    remaining -= n;
                }
                if (!ok) break;
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                long payloadOffset = pos + RecordHeaderSize;
                pos = payloadOffset + payloadLength;
                if (Hash256.FromBytes(sha.Hash!) == expected && !_index.ContainsKey(expected)) {
                    _index.Add(expected, new Location(payloadOffset, (int) payloadLength));
                }
                // a record whose payload hash mismatches is skipped and the scan continues
            }

            if (pos != length) {
                _stream.SetLength(pos);
                _stream.Flush(true);
            }
            _stream.Seek(0, SeekOrigin.End);
        }

        private bool ReadExactly(byte[] buffer, int offset, int count)
        {
            while (count > 0) {
                int n = _stream.Read(buffer, offset, count);
                if (n <= 0) return false;
                offset += n;
                count -= n;
            }
            return true;
        }
    }
}
