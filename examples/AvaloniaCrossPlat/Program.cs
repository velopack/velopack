using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Velopack;

namespace AvaloniaCrossPlat;

class Program
{
    public static string UpdateUrl { get; private set; }

    public static MemoryLogger Log { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try {
            // Logging is essential for debugging! Ideally you should write it to a file.
            Log = new MemoryLogger();

            // It's important to Run() the VelopackApp as early as possible in app startup.
            VelopackApp.Build()
                .Run(Log);

            // This is purely for demonstration purposes, we get the update URL from a
            // property defined by MSBuild, so we can locate the local releases directory.
            // In your production app, this should point to your update server.
            UpdateUrl = Assembly.GetEntryAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(x => x.Key == "AvaloniaSampleReleaseDir")
                .Single().Value;

            // Now it's time to run Avalonia
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        } catch (Exception ex) {
            string message = "Unhandled exception: " + ex.ToString();
            Console.WriteLine(message);
            throw;
        }
    }

    // Avalonia configuration, don't remove method; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
