#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Velopack.Util;

namespace Velopack.Packaging.Unix;

/// <summary>
/// Appends/reads the AppImage channel-override trailer ([magic][u16-LE length][channel]).
/// Pure managed file I/O — usable headless on Linux server workers (same pattern as
/// SetupBundle). Parsing delegates to Velopack.Util.AppImageChannelOverride (lib-csharp,
/// via InternalsVisibleTo) so the format lives in exactly one C# implementation.
/// Only apps shipped with an SDK version at or above the release containing the trailer
/// reader will honor the override; older Linux installs silently keep the manifest channel.
/// </summary>
public static class AppImageChannelTrailer
{
    /// <summary>
    /// The first SDK release whose Linux locators ship the trailer reader (CONTRACTS §1.6
    /// TRAILER_MIN_SDK_VERSION — the version floor for the channel-override feature). Packages
    /// whose nuspec &lt;velopackVersion&gt; is missing or below this version may ignore the
    /// override. The velopack.api repo copies this value into its promotion config
    /// (Promotions:TrailerMinSdkVersion) to drive the old-SDK warning.
    /// </summary>
    public const string TRAILER_MIN_SDK_VERSION = "1.2.0";

    /// <summary>
    /// Strips any existing EOF-anchored trailers, then appends a new one for
    /// <paramref name="channel"/> (normalized to lowercase). Throws <see cref="ArgumentException"/>
    /// on an invalid channel, and IOException-family exceptions on file errors.
    /// </summary>
    public static void WriteChannelOverride(string appImagePath, string channel)
    {
        if (channel == null) throw new ArgumentNullException(nameof(channel));
        // note: no Trim() — CONTRACTS §1.3 requires writers to REJECT channels that do not
        // match ^[\x21-\x7E]{1,255}$ after lowercasing, so whitespace-carrying input throws
        // (via the byte validation below) rather than being silently normalized.
        channel = channel.ToLowerInvariant();
        var channelBytes = Encoding.UTF8.GetBytes(channel);
        if (channelBytes.Length < 1 || channelBytes.Length > AppImageChannelOverride.MaxChannelLength) {
            throw new ArgumentException(
                $"Channel must be between 1 and {AppImageChannelOverride.MaxChannelLength} bytes long (was {channelBytes.Length}).",
                nameof(channel));
        }

        foreach (var b in channelBytes) {
            if (!AppImageChannelOverride.IsValidChannelByte(b)) {
                throw new ArgumentException(
                    $"Channel '{channel}' contains invalid characters. Only printable ASCII (0x21-0x7E, no whitespace) is allowed.",
                    nameof(channel));
            }
        }

        // strip any existing EOF-anchored trailers so re-promotion does not grow the file unboundedly.
        // an *invalid* trailer at EOF is not stripped — the reader's last-valid-wins rule means the
        // trailer appended below still resolves correctly.
        using (var fs = File.Open(appImagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            while (TryFindEofAnchoredTrailer(fs, out var trailerStart)) {
                fs.SetLength(trailerStart);
            }
        }

        using (var fs = File.Open(appImagePath, FileMode.Append, FileAccess.Write, FileShare.None)) {
            fs.Write(AppImageChannelOverride.Magic, 0, AppImageChannelOverride.Magic.Length);
            fs.WriteByte((byte) (channelBytes.Length & 0xFF)); // u16 little-endian length
            fs.WriteByte((byte) (channelBytes.Length >> 8));
            fs.Write(channelBytes, 0, channelBytes.Length);
        }

        if (!TryReadChannelOverride(appImagePath, out var written) || written != channel) {
            throw new InvalidOperationException("Internal logic error writing AppImage channel trailer.");
        }
    }

    /// <summary>
    /// Reads the current channel override, if any. Never throws on parse problems;
    /// returns false (and a null channel) when no valid trailer is present.
    /// </summary>
    public static bool TryReadChannelOverride(string appImagePath, [NotNullWhen(true)] out string? channel)
    {
        channel = AppImageChannelOverride.TryReadFromFile(appImagePath, null);
        return channel != null;
    }

    private static bool TryFindEofAnchoredTrailer(FileStream fs, out long trailerStart)
    {
        trailerStart = 0;
        long fileLen = fs.Length;
        if (fileLen < AppImageChannelOverride.HeaderSize + 1) return false;

        int win = (int) Math.Min(AppImageChannelOverride.HeaderSize + AppImageChannelOverride.MaxChannelLength, fileLen);
        long winStart = fileLen - win;
        var tail = new byte[win];
        fs.Seek(winStart, SeekOrigin.Begin);
        int total = 0;
        while (total < win) {
            int read = fs.Read(tail, total, win - total);
            if (read <= 0) throw new EndOfStreamException("Unexpected end of file reading AppImage tail.");
            total += read;
        }

        var magic = AppImageChannelOverride.Magic;
        for (int pos = win - AppImageChannelOverride.HeaderSize; pos >= 0; pos--) {
            bool match = true;
            for (int i = 0; i < magic.Length; i++) {
                if (tail[pos + i] != magic[i]) {
                    match = false;
                    break;
                }
            }

            if (!match) continue;
            int length = tail[pos + 32] | (tail[pos + 33] << 8);
            if (length < 1 || length > AppImageChannelOverride.MaxChannelLength) continue;
            if (winStart + pos + AppImageChannelOverride.HeaderSize + length != fileLen) continue; // not EOF-anchored
            bool valid = true;
            for (int i = 0; i < length; i++) {
                if (!AppImageChannelOverride.IsValidChannelByte(tail[pos + AppImageChannelOverride.HeaderSize + i])) {
                    valid = false;
                    break;
                }
            }

            if (!valid) continue;
            trailerStart = winStart + pos;
            return true;
        }

        return false;
    }
}
