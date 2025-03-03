using System;
using Avalonia;
using Velopack;

namespace CSharpAvalonia;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try {
            // It's important to Run() the VelopackApp as early as possible in app startup.
            VelopackApp.Build()
                .OnFirstRun((v) => { /* Your first run code here */ })
                .Run();

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
