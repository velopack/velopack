using UIKit;
using Velopack;

namespace UnoSample.MacCatalyst;
public class EntryPoint
{
    // This is the main entry point of the application.
    public static void Main(string[] args)
    {
        // It's important to Run() the VelopackApp as early as possible in app startup.
        VelopackApp.Build()
            .WithFirstRun((v) => { /* Your first run code here */ })
            .Run();

        // if you want to use a different Application Delegate class from "AppDelegate"
        // you can specify it here.
        UIApplication.Main(args, null, typeof(App));
    }
}
