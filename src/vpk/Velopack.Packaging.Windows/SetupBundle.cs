using System.IO.MemoryMappedFiles;

namespace Velopack.Packaging.Windows;

public static class SetupBundle
{
    public static bool IsBundle(string setupPath, out long bundleOffset, out long bundleLength)
    {
        byte[] bundleSignature = {
            // 64 bytes represent the bundle signature: SHA-256 for "squirrel bundle"
            0x94, 0xf0, 0xb1, 0x7b, 0x68, 0x93, 0xe0, 0x29,
            0x37, 0xeb, 0x34, 0xef, 0x53, 0xaa, 0xe7, 0xd4,
            0x2b, 0x54, 0xf5, 0x70, 0x7e, 0xf5, 0xd6, 0xf5,
            0x78, 0x54, 0x98, 0x3e, 0x5e, 0x94, 0xed, 0x7d
        };

        long offset = 0;
        long length = 0;

        void FindBundleHeader()
        {
            using var memoryMappedFile = MemoryMappedFile.CreateFromFile(setupPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using MemoryMappedViewAccessor accessor = memoryMappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            int position = SearchInFile(accessor, bundleSignature);
            if (position == -1) {
                throw new Exception("PlaceHolderNotFoundInAppHostException");
            }

            offset = accessor.ReadInt64(position - 16);
            length = accessor.ReadInt64(position - 8);
        }

        Utility.Retry(FindBundleHeader);

        bundleOffset = offset;
        bundleLength = length;

        return bundleOffset != 0 && bundleLength != 0;
    }

    public static long CreatePackageBundle(string setupPath, string packagePath)
    {
        long bundleOffset, bundleLength;
        Stream pkgStream = null, setupStream = null;

        try {
            pkgStream = Utility.Retry(() => File.OpenRead(packagePath), retries: 10);
            setupStream = Utility.Retry(() => File.Open(setupPath, FileMode.Append, FileAccess.Write), retries: 10);
            bundleOffset = setupStream.Position;
            bundleLength = pkgStream.Length;
            pkgStream.CopyTo(setupStream);
        } finally {
            pkgStream?.Dispose();
            setupStream?.Dispose();
        }

        byte[] placeholder = {
            // 8 bytes represent the package offset 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // 8 bytes represent the package length 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // 64 bytes represent the bundle signature: SHA-256 for "squirrel bundle"
            0x94, 0xf0, 0xb1, 0x7b, 0x68, 0x93, 0xe0, 0x29,
            0x37, 0xeb, 0x34, 0xef, 0x53, 0xaa, 0xe7, 0xd4,
            0x2b, 0x54, 0xf5, 0x70, 0x7e, 0xf5, 0xd6, 0xf5,
            0x78, 0x54, 0x98, 0x3e, 0x5e, 0x94, 0xed, 0x7d
        };

        var data = new byte[16];
        Array.Copy(BitConverter.GetBytes(bundleOffset), data, 8);
        Array.Copy(BitConverter.GetBytes(bundleLength), 0, data, 8, 8);

        // replace the beginning of the placeholder with the bytes from 'data'
        RetryOnIOError(() =>
            SearchAndReplace(setupPath, placeholder, data, pad0s: false));

        // memory-mapped write does not updating last write time
        RetryOnIOError(() =>
            File.SetLastWriteTimeUtc(setupPath, DateTime.UtcNow));

        if (!IsBundle(setupPath, out var offset, out var length))
            throw new InvalidOperationException("Internal logic error writing setup bundle.");

        return bundleOffset;
    }

    internal static unsafe void SearchAndReplace(
       MemoryMappedViewAccessor accessor,
       byte[] searchPattern,
       byte[] patternToReplace,
       bool pad0s = true)
    {
        byte* pointer = null;

        try {
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            byte* bytes = pointer + accessor.PointerOffset;

            int position = KMPSearch(searchPattern, bytes, accessor.Capacity);
            if (position < 0) {
                throw new Exception("PlaceHolderNotFoundInAppHostException");
            }

            accessor.WriteArray(
                position: position,
                array: patternToReplace,
                offset: 0,
                count: patternToReplace.Length);

            if (pad0s) {
                Pad0(searchPattern, patternToReplace, bytes, position);
            }
        } finally {
            if (pointer != null) {
                accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
    }

    private static unsafe void Pad0(byte[] searchPattern, byte[] patternToReplace, byte* bytes, int offset)
    {
        if (patternToReplace.Length < searchPattern.Length) {
            for (int i = patternToReplace.Length; i < searchPattern.Length; i++) {
                bytes[i + offset] = 0x0;
            }
        }
    }

    public static unsafe void SearchAndReplace(
        string filePath,
        byte[] searchPattern,
        byte[] patternToReplace,
        bool pad0s = true)
    {
        using (var mappedFile = MemoryMappedFile.CreateFromFile(filePath)) {
            using (var accessor = mappedFile.CreateViewAccessor()) {
                SearchAndReplace(accessor, searchPattern, patternToReplace, pad0s);
            }
        }
    }

    public static unsafe int SearchInFile(MemoryMappedViewAccessor accessor, byte[] searchPattern)
    {
        var safeBuffer = accessor.SafeMemoryMappedViewHandle;
        return KMPSearch(searchPattern, (byte*) safeBuffer.DangerousGetHandle(), (int) safeBuffer.ByteLength);
    }

    public static unsafe int SearchInFile(string filePath, byte[] searchPattern)
    {
        using (var mappedFile = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read)) {
            using (var accessor = mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read)) {
                return SearchInFile(accessor, searchPattern);
            }
        }
    }

    // See: https://en.wikipedia.org/wiki/Knuth%E2%80%93Morris%E2%80%93Pratt_algorithm
    private static int[] ComputeKMPFailureFunction(byte[] pattern)
    {
        int[] table = new int[pattern.Length];
        if (pattern.Length >= 1) {
            table[0] = -1;
        }
        if (pattern.Length >= 2) {
            table[1] = 0;
        }

        int pos = 2;
        int cnd = 0;
        while (pos < pattern.Length) {
            if (pattern[pos - 1] == pattern[cnd]) {
                table[pos] = cnd + 1;
                cnd++;
                pos++;
            } else if (cnd > 0) {
                cnd = table[cnd];
            } else {
                table[pos] = 0;
                pos++;
            }
        }
        return table;
    }

    // See: https://en.wikipedia.org/wiki/Knuth%E2%80%93Morris%E2%80%93Pratt_algorithm
    private static unsafe int KMPSearch(byte[] pattern, byte* bytes, long bytesLength)
    {
        int m = 0;
        int i = 0;
        int[] table = ComputeKMPFailureFunction(pattern);

        while (m + i < bytesLength) {
            if (pattern[i] == bytes[m + i]) {
                if (i == pattern.Length - 1) {
                    return m;
                }
                i++;
            } else {
                if (table[i] > -1) {
                    m = m + i - table[i];
                    i = table[i];
                } else {
                    m++;
                    i = 0;
                }
            }
        }

        return -1;
    }

    public static void CopyFile(string sourcePath, string destinationPath)
    {
        var destinationDirectory = new FileInfo(destinationPath).Directory.FullName;
        if (!Directory.Exists(destinationDirectory)) {
            Directory.CreateDirectory(destinationDirectory);
        }

        // Copy file to destination path so it inherits the same attributes/permissions.
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    internal static void WriteToStream(MemoryMappedViewAccessor sourceViewAccessor, FileStream fileStream, long length)
    {
        int pos = 0;
        int bufSize = 16384; //16K

        byte[] buf = new byte[bufSize];
        length = Math.Min(length, sourceViewAccessor.Capacity);
        do {
            int bytesRequested = Math.Min((int) length - pos, bufSize);
            if (bytesRequested <= 0) {
                break;
            }

            int bytesRead = sourceViewAccessor.ReadArray(pos, buf, 0, bytesRequested);
            if (bytesRead > 0) {
                fileStream.Write(buf, 0, bytesRead);
                pos += bytesRead;
            }
        }
        while (true);
    }

    public const int NumberOfRetries = 500;
    public const int NumMilliSecondsToWait = 100;

    public static void RetryOnIOError(Action func)
    {
        for (int i = 1; i <= NumberOfRetries; i++) {
            try {
                func();
                break;
            } catch (IOException) when (i < NumberOfRetries) {
                Thread.Sleep(NumMilliSecondsToWait);
            }
        }
    }

    public static void RetryOnWin32Error(Action func)
    {
        static bool IsKnownIrrecoverableError(int hresult)
        {
            // Error codes are defined in winerror.h
            // The error code is stored in the lowest 16 bits of the HResult

            switch (hresult & 0xffff) {
            case 0x00000001: // ERROR_INVALID_FUNCTION
            case 0x00000002: // ERROR_FILE_NOT_FOUND
            case 0x00000003: // ERROR_PATH_NOT_FOUND
            case 0x00000006: // ERROR_INVALID_HANDLE
            case 0x00000008: // ERROR_NOT_ENOUGH_MEMORY
            case 0x0000000B: // ERROR_BAD_FORMAT
            case 0x0000000E: // ERROR_OUTOFMEMORY
            case 0x0000000F: // ERROR_INVALID_DRIVE
            case 0x00000012: // ERROR_NO_MORE_FILES
            case 0x00000035: // ERROR_BAD_NETPATH
            case 0x00000057: // ERROR_INVALID_PARAMETER
            case 0x00000071: // ERROR_NO_MORE_SEARCH_HANDLES
            case 0x00000072: // ERROR_INVALID_TARGET_HANDLE
            case 0x00000078: // ERROR_CALL_NOT_IMPLEMENTED
            case 0x0000007B: // ERROR_INVALID_NAME
            case 0x0000007C: // ERROR_INVALID_LEVEL
            case 0x0000007D: // ERROR_NO_VOLUME_LABEL
            case 0x0000009A: // ERROR_LABEL_TOO_LONG
            case 0x000000A0: // ERROR_BAD_ARGUMENTS
            case 0x000000A1: // ERROR_BAD_PATHNAME
            case 0x000000CE: // ERROR_FILENAME_EXCED_RANGE
            case 0x000000DF: // ERROR_FILE_TOO_LARGE
            case 0x000003ED: // ERROR_UNRECOGNIZED_VOLUME
            case 0x000003EE: // ERROR_FILE_INVALID
            case 0x00000651: // ERROR_DEVICE_REMOVED
                return true;

            default:
                return false;
            }
        }

        for (int i = 1; i <= NumberOfRetries; i++) {
            try {
                func();
                break;
            } catch (HResultException hrex)
                  when (i < NumberOfRetries && !IsKnownIrrecoverableError(hrex.Win32HResult)) {
                Thread.Sleep(NumMilliSecondsToWait);
            }
        }
    }

    public class HResultException : Exception
    {
        public readonly int Win32HResult;
        public HResultException(int hResult) : base(hResult.ToString("X4"))
        {
            Win32HResult = hResult;
        }
    }
}