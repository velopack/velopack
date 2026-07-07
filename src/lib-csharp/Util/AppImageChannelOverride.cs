using System;
using System.IO;
using System.Text;
using Velopack.Logging;

namespace Velopack.Util
{
    /// <summary>
    /// Reads the AppImage channel-override trailer: [MAGIC(32)][LENGTH u16-LE(2)][CHANNEL utf8(1..255)]
    /// appended after the squashfs. Written server-side during channel promotion (and by
    /// Velopack.Packaging.Unix.AppImageChannelTrailer). Only AppImages produced/updated by SDK
    /// versions at or above the release shipping this type can read the override (version floor) —
    /// older Linux installs silently keep the manifest channel.
    /// Keep byte-for-byte in sync with lib-rust/src/locator.rs (APPIMAGE_CHANNEL_MAGIC).
    /// </summary>
    internal static class AppImageChannelOverride
    {
        // SHA-256 of "velopack appimage channel override". Mirrors the SetupBundle signature
        // precedent (SetupBundle.cs). MUST match lib-rust locator.rs and the velopack.api worker.
        internal static readonly byte[] Magic = new byte[] {
            0xde, 0xed, 0x1b, 0xad, 0x30, 0x15, 0xb1, 0x96,
            0x9e, 0x6e, 0xbf, 0x7d, 0x09, 0x3f, 0x5d, 0xca,
            0x6c, 0x6c, 0x52, 0xa1, 0xa0, 0xa2, 0x57, 0x57,
            0x19, 0x91, 0x62, 0x83, 0x11, 0xd8, 0x03, 0x51,
        };

        internal const int ScanWindowSize = 1024;   // trailer max 34+255=289; window gives slack
        internal const int MaxChannelLength = 255;  // LENGTH valid range 1..=255
        internal const int HeaderSize = 34;         // 32 magic + 2 length

        /// <summary>
        /// Reads the last <see cref="ScanWindowSize"/> bytes of the file and parses per the contract.
        /// Never throws; returns null on any failure (missing file, IO error, no valid trailer).
        /// </summary>
        internal static string? TryReadFromFile(string appImagePath, IVelopackLogger? log)
        {
            try {
                var fi = new FileInfo(appImagePath);
                if (!fi.Exists || fi.Length < HeaderSize + 1) return null;
                int win = (int) Math.Min(ScanWindowSize, fi.Length);
                var buffer = IoUtil.Retry(
                    () => {
                        var buf = new byte[win];
                        using var fs = new FileStream(appImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        fs.Seek(-win, SeekOrigin.End);
                        int total = 0;
                        while (total < win) {
                            int read = fs.Read(buf, total, win - total);
                            if (read <= 0) throw new EndOfStreamException("Unexpected end of file reading AppImage tail.");
                            total += read;
                        }
                        return buf;
                    },
                    logger: log);
                return ParseWindow(buffer);
            } catch (Exception ex) {
                log?.Warn($"Failed reading AppImage channel-override trailer from '{appImagePath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Scans a tail window of the file for the trailer, from the end toward the start,
        /// returning the channel of the last valid trailer, or null. Malformed occurrences are skipped.
        /// </summary>
        internal static string? ParseWindow(byte[] window)
        {
            if (window == null || window.Length < HeaderSize + 1) return null;
            for (int pos = window.Length - HeaderSize; pos >= 0; pos--) {
                if (!MatchesMagicAt(window, pos)) continue;
                int length = window[pos + 32] | (window[pos + 33] << 8); // u16 little-endian
                if (length < 1 || length > MaxChannelLength) continue;
                if (pos + HeaderSize + length > window.Length) continue; // truncated channel
                bool valid = true;
                for (int i = 0; i < length; i++) {
                    if (!IsValidChannelByte(window[pos + HeaderSize + i])) {
                        valid = false;
                        break;
                    }
                }

                if (!valid) continue;
                return Encoding.UTF8.GetString(window, pos + HeaderSize, length); // verbatim; no folding/trim
            }

            return null;
        }

        /// <summary> True if the byte is allowed in a channel name (printable ASCII, no whitespace/control). </summary>
        internal static bool IsValidChannelByte(byte b) => b >= 0x21 && b <= 0x7E;

        private static bool MatchesMagicAt(byte[] window, int pos)
        {
            for (int i = 0; i < Magic.Length; i++) {
                if (window[pos + i] != Magic[i]) return false;
            }

            return true;
        }
    }
}
