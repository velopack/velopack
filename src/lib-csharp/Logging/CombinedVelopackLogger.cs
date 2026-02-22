using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Velopack.Logging
{
    internal class CombinedVelopackLogger : IVelopackLogger, IDisposable
    {
        private readonly List<IVelopackLogger> _loggers = new();

        public CombinedVelopackLogger(params IVelopackLogger?[] loggers)
        {
            foreach (var logger in loggers) {
                if (logger != null) {
                    _loggers.Add(logger);
                }
            }
        }

        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            foreach (var logger in _loggers) {
                try {
                    logger.Log(logLevel, message, exception);
                } catch (Exception ex) {
                    Debug.WriteLine($"Error logging to {logger.GetType().Name} ({ex}) {Environment.NewLine} [{logLevel}] {message}");
                }
            }
        }

        public void Add(IVelopackLogger? logger)
        {
            if (logger != null) {
                _loggers.Add(logger);
            }
        }

        public void Dispose()
        {
            var localLoggers = _loggers.ToArray();
            _loggers.Clear();

            foreach (var logger in localLoggers) {
                try {
                    if (logger is IDisposable disposable)
                        disposable.Dispose();
                } catch (Exception ex) {
                    Debug.WriteLine($"Error disposing {logger.GetType().Name} ({ex})");
                }
            }
        }
    }
}