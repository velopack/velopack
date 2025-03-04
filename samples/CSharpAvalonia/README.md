# AvaloniaCrossPlat
_Prerequisites: vpk command line tool installed_

This app demonstrates how to use Avalonia to provide a desktop UI, installer, and updates for Mac, Linux, and Windows.

You can run this sample by executing the build script with a version number (eg. `build-win.bat 1.0.0`).
There are build scripts provided for each platform (`build-win.bat`, `build-linux.sh`, `build-osx.bat`).

Once built, you can install the app - build more updates, and then test updates and so forth. The sample app will check the local release dir for new update packages. 

In your production apps, you should deploy your updates to some kind of update server instead.

On Linux, there is no installer, since the program is shipped as a `.AppImage`, it is only portable - however it can still update itself by replacing it's own `.AppImage` (even if that `.AppImage` is inside privileged directories)

## Avalonia Implementation Notes
The Avalonia Template will generate a `Program.Main()` for you. You need to be careful when editing this file as to not break the Avalonia designer. You must not delete the `BuildAvaloniaApp()` function, but you must add the `VelopackApp` builder to the `Main()` method. For example:

```cs
class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called. 
    // things aren't initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
```

## Testing changes to Velopack
This project has a folder of development build scripts (e.g. `.\dev-scripts\build-win.bat`) which will create a release in same way as the main scripts, except with a project reference to Velopack, and it will invoke the local vpk tool as well. 

If you have made a change to Velopack and would like to test it in the sample app, these are the scripts you should run instead.