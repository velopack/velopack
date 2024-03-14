using System;
using Avalonia;
using Velopack;

namespace AvaloniaCrossPlat;

class Program
{
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
                .WithFirstRun((v) => { /* Your first run code here */ })
                .Run(Log);

            // Now it's time to run Avalonia
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

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
