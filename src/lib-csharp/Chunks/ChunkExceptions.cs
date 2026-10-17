using System;

namespace Velopack.Chunks
{
    /// <summary>The remote chunk index failed validation (§2.8); the staged directory is deleted before the classic fallback.</summary>
    internal class ChunkIndexInvalidException : Exception
    {
        public ChunkIndexInvalidException(string message) : base(message)
        {
        }

        public ChunkIndexInvalidException(string message, Exception? innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>A fatal fetch failure (§5.6): the chunk path is abandoned, the staged directory is kept for resume.</summary>
    internal class ChunkFetchFatalException : Exception
    {
        public ChunkFetchFatalException(string message) : base(message)
        {
        }

        public ChunkFetchFatalException(string message, Exception? innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>A fetched chunk did not inflate to the expected length / SHA-256 (transient, retried per request).</summary>
    internal class ChunkMismatchException : Exception
    {
        public ChunkMismatchException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// A retryable range request failure with an optional <c>Retry-After</c> hint (HTTP 408/425/429/5xx, short body).
    /// </summary>
    internal class RangeTransientException : Exception
    {
        public TimeSpan? RetryAfter { get; }

        public RangeTransientException(string message, TimeSpan? retryAfter = null) : base(message)
        {
            RetryAfter = retryAfter;
        }

        public RangeTransientException(string message, Exception? innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// The cached redirect target answered 401/403/404 and has been invalidated (single-flight re-resolve); the request
    /// should be retried, which counts as an attempt and as one "refresh" against the refresh budget.
    /// </summary>
    internal class RangeRefreshException : Exception
    {
        /// <summary>
        /// True when this request is the one that invalidated the cached URL (one per expired generation); concurrent
        /// requests which merely observed the same expiry carry false, so the refresh budget counts re-resolutions.
        /// </summary>
        public bool Invalidated { get; }

        public RangeRefreshException(string message, bool invalidated) : base(message)
        {
            Invalidated = invalidated;
        }
    }

    /// <summary><c>Update.exe assemble</c> exited 2 (invalid input); the staged directory is deleted.</summary>
    internal class AssembleInvalidException : Exception
    {
        public AssembleInvalidException(string message) : base(message)
        {
        }
    }

    /// <summary><c>Update.exe assemble</c> exited 4 (IO error, disk full, readback mismatch); the staged directory is deleted.</summary>
    internal class AssembleIoException : Exception
    {
        public AssembleIoException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// The updater exited with an unknown code without printing any protocol line: it does not know the
    /// <c>assemble</c> command. The chunk path is disabled for the <see cref="UpdateManager"/> instance.
    /// </summary>
    internal class AssembleUnsupportedException : Exception
    {
        public AssembleUnsupportedException(string message) : base(message)
        {
        }
    }

    /// <summary>Any other <c>Update.exe assemble</c> failure (unknown exit code, inactivity timeout, too many missing rounds).</summary>
    internal class AssembleFailedException : Exception
    {
        public AssembleFailedException(string message) : base(message)
        {
        }

        public AssembleFailedException(string message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
