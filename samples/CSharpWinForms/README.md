# VeloWinFormsSample
_Prerequisites: vpk command line tool installed_

This app demonstrates how to use WinForms to provide a desktop UI, installer, and updates for Windows only.

You can run this sample by executing the build script with a version number: `build.bat 1.0.0`. Once built, you can install the app - build more updates, and then test updates and so forth. The sample app will check the local release dir for new update packages. 

In your production apps, you should deploy your updates to some kind of update server instead.

## WinForms Implementation Notes
The Velopack startup bootstrapping should be placed as early as possible in the Main method.

```csharp
VelopackApp.Build()
    .WithFirstRun((v) => { /* Your first run code here */ })
    .Run(Log);
```

This sample explicitly sets the AssemblyName in the csproj file to match the Velpoack packId (`-u` option). This is not required, but demonstrates handling the case where the app name and the packId are different.
