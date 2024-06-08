using Microsoft.UI.Dispatching;
using Velopack;

namespace UnoSample;
public static class Program
{
    // https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/guides/applifecycle?WT.mc_id=DT-MVP-5003472#single-instancing-in-main-or-wwinmain
    [STAThread]
    public static void Main(string[] args)
    {
        // It's important to Run() the VelopackApp as early as possible in app startup.
        VelopackApp.Build()
            .WithFirstRun((v) => { /* Your first run code here */ })
            .Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
