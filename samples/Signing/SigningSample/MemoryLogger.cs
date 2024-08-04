using System.Text;
using Microsoft.Extensions.Logging;

namespace SigningSample
{
    public class LogUpdatedEventArgs : EventArgs
    {
        public string Text { get; set; }
    }

    public class MemoryLogger : ILogger
    {
        public event EventHandler<LogUpdatedEventArgs> LogUpdated;
        private readonly StringBuilder _sb = new StringBuilder();

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            lock (_sb) {
                var message = formatter(state, exception);
                if (exception != null) message += "\n" + exception.ToString();
                Console.WriteLine("log: " + message);
                _sb.AppendLine(message);
                LogUpdated?.Invoke(this, new LogUpdatedEventArgs { Text = _sb.ToString() });
            }
        }

        public override string ToString()
        {
            lock (_sb) {
                return _sb.ToString();
            }
        }
    }
}
