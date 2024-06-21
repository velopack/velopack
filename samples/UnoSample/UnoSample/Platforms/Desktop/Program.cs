using Uno.UI.Runtime.Skia;
using Velopack;

namespace UnoSample;
public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // It's important to Run() the VelopackApp as early as possible in app startup.
        VelopackApp.Build()
            .WithFirstRun((v) => { /* Your first run code here */ })
            .Run();

#if (!useDependencyInjection && useLoggingFallback)
        App.InitializeLogging();

#endif
        var host = SkiaHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWindows()
            .Build();

        host.Run();
    }
}
