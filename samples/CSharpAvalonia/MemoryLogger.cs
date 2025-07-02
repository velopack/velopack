using System;
using System.Text;
using Velopack.Logging;

namespace CSharpAvalonia;

public class LogUpdatedEventArgs : EventArgs
{
    public string Text { get; set; }
}

public class MemoryLogger : IVelopackLogger
{
    public event EventHandler<LogUpdatedEventArgs> LogUpdated;
    private readonly StringBuilder _sb = new();

    public override string ToString()
    {
        lock (_sb) {
            return _sb.ToString();
        }
    }

    public void Log(VelopackLogLevel logLevel, string message, Exception exception)
    {
        if (logLevel < VelopackLogLevel.Information) return;
        lock (_sb) {
            message = $"{logLevel}: {message}";
            if (exception != null) message += "\n" + exception.ToString();
            Console.WriteLine("log: " + message);
            _sb.AppendLine(message);
            LogUpdated?.Invoke(this, new LogUpdatedEventArgs { Text = _sb.ToString() });
        }
    }
}