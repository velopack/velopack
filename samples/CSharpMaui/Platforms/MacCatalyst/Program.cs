using UIKit;
using Velopack;

namespace CSharpMaui;

public class Program
{
    static void Main(string[] args)
    {
        // It's important to Run() the VelopackApp as early as possible in app startup.
        VelopackApp.Build()
            .OnFirstRun((v) => { /* Your first run code here */ })
            .SetLogger(MauiProgram.Log)
            .Run();

        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
