using Microsoft.UI.Dispatching;
using Velopack;

namespace CSharpMaui.WinUI;

public static class Program
{
    // Replaces the Main() WinUI generates (see DISABLE_XAML_GENERATED_MAIN in the csproj),
    // so that Velopack can run before the XAML runtime starts.
    [STAThread]
    static void Main(string[] args)
    {
        // It's important to Run() the VelopackApp as early as possible in app startup.
        VelopackApp.Build()
            .OnFirstRun((v) => { /* Your first run code here */ })
            .SetLogger(MauiProgram.Log)
            .Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p => {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
