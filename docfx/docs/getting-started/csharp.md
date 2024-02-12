*Applies to: Windows, MacOS, Linux*

# Getting Started: C# / .NET

1. Install the command line tool `vpk`:
   ```cmd
   dotnet tool update -g vpk
   ```
2. Install the  [Velopack NuGet Package](https://www.nuget.org/packages/velopack) in your main project:
   ```cmd
   dotnet add package Velopack
   ```
3. Configure your Velopack app at the beginning of `Program.Main`:
   ```cs
   static void Main(string[] args)
   {
       VelopackApp.Build().Run();
       // ... your other startup code below
   }
   ```
4. Add automatic updating to your app:
   ```cs
   private static async Task UpdateMyApp()
   {
       var mgr = new UpdateManager("https://the.place/you-host/updates");

       // check for new version
       var newVersion = await mgr.CheckForUpdatesAsync();
       if (newVersion == null)
           return; // no update available

       // download new version
       await mgr.DownloadUpdatesAsync(newVersion);

       // install new version and restart app
       mgr.ApplyUpdatesAndRestart(newVersion);
   }
   ```
5. Publish dotnet and build your first Velopack release! 🎉
   ```cmd
   dotnet publish -c Release --self-contained -r win-x64 -o .\publish
   vpk pack -u YourAppId -v 1.0.0 -p .\publish -e yourMainApp.exe
   ```
6. Upload the files created by Velopack to `https://the.place/you-host/updates`

If you're not sure how these instructions fit into your app, check the example apps for common scenarios such as WPF or Avalonia.