*Applies to: Windows, MacOS, Linux*

# Debugging Velopack

## Logging
All parts of Velopack have logging built in to help troubleshoot issues, and you should provide these logs when opening a GitHub issue about a potential bug.

### UpdateManager / In your application
You should provide an instance of `Microsoft.Extensions.Logging.ILogger` to `VelopackApp.Run(ILogger)` and to `UpdateManager` to record potential issues. If you are not using Microsoft Hosting or Logging already, it is very simple to implement this interface yourself and log to a file, or integrate with another logging framework. 

For example:
```cs
using Microsoft.Extensions.Logging;

// ...

class ConsoleLogger : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        => Console.WriteLine(formatter(state, exception));
}

// ...

new UpdateManager("https://path.to/your-updates", logger: new ConsoleLogger());
```

### Windows
Running Update.exe will log most output to it's base directory as `Velopack.log`. Setup.exe will not log to file by default. However, you can override the log location for both binaries with the `--log {path}` parameter. You can also use the `--verbose` flag to capture debug/trace output to log. Unfortunately, on Windows, to avoid showing up as a console window, these binaries are compiled as a WinExe and there will be no console output by default.  Please see the [command line reference](cli.md) for a comprehensive list of arguments supported.

### MacOS / Linux
All logs will be sent to `/tmp/velopack.log`.

## Advanced Debugging
The debug builds of Velopack binaries have additional logging/debugging capabilities, and will produce console output. In some instances, it may be useful to [compile Velopack](../compiling.md) for your platform, and replace the release binaries of Setup.exe and Update.exe with debug versions. 

If your issue is with package building, after building the rust binaries in Debug mode, it can also be useful to run the Velopack.Vpk project from Visual Studio with your intended command line arguments rather than running the `vpk` tool directly.

If doing this has not helped, you may need to debug and step through the rust binaries - for which I recommend the CodeLLDB VSCode extension.