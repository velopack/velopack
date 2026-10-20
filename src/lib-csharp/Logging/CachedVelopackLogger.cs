using System;
using System.Collections.Generic;

namespace Velopack.Logging
{
    internal class CachedVelopackLogger : IVelopackLogger, IDisposable
    {
        private readonly List<CachedLogEntry> _cache = new();
        private readonly IVelopackLogger _logger;
        private readonly object _lock = new();
        private bool _committed;

        public CachedVelopackLogger(IVelopackLogger logger)
        {
            _logger = logger;
        }

        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            lock (_lock) {
                if (_committed) {
                    _logger.Log(logLevel, message, exception);
                } else {
                    _cache.Add(new CachedLogEntry(logLevel, message, exception));
                }
            }
        }

        private void Commit()
        {
            lock (_lock) {
                if (_committed) {
                    return;
                }

                foreach (var entry in _cache) {
                    _logger.Log(entry.LogLevel, entry.Message, entry.Exception);
                }

                _cache.Clear();
                _committed = true;
            }
        }

        public void Dispose()
        {
            Commit();
        }

        private sealed class CachedLogEntry
        {
            public VelopackLogLevel LogLevel { get; }
            public string? Message { get; }
            public Exception? Exception { get; }

            public CachedLogEntry(VelopackLogLevel logLevel, string? message, Exception? exception)
            {
                LogLevel = logLevel;
                Message = message;
                Exception = exception;
            }
        }
    }
}