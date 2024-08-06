using Uno.UI.Runtime.Skia;

namespace UnoSample;
public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
