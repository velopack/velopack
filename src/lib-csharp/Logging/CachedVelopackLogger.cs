using System;
using System.Collections.Generic;

namespace Velopack.Logging
{
    internal class CachedVelopackLogger : IVelopackLogger, IDisposable
    {
        private readonly List<(VelopackLogLevel logLevel, string? message, Exception? exception)> _cache = new();
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
                    _cache.Add((logLevel, message, exception));
                }
            }
        }

        private void Commit()
        {
            lock (_lock) {
                if (_committed) {
                    return;
                }

                foreach (var (logLevel, message, exception) in _cache) {
                    _logger.Log(logLevel, message, exception);
                }

                _cache.Clear();
                _committed = true;
            }
        }

        public void Dispose()
        {
            Commit();
        }
    }
}