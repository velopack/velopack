*Applies to: Windows, MacOS, Linux*

# Updating Overview
To integrate Velopack into your application, you *must* initialise the Velopack as early as possible in app startup, and you *should* add update checking code somewhere.

For .NET applications, you should first install the [Velopack Nuget Package](https://nuget.org/packages/velopack).

## Application Startup
Velopack requires you add some code to your application startup to handle hooks. This is because Velopack will run your main binary at certain stages of the install/update process with special arguments, to allow you to customise behavior. It expects your app to respond to these arguments in the right way and then exit as soon as possible. 

The simplest/minimal way to handle this properly is to add the SDK startup code to your `Main()` method.

```cs
static void Main(string[] args)
{
    VelopackApp.Build().Run();
    // ... your other startup code below
}
```

There are a variety of options / callbacks you can specify here to customise Velopack, for example:

```cs
static void Main(string[] args)
{
    ILogger log = CreateLogger();
    VelopackApp.Build()
        .WithBeforeUninstallFastCallback((v) => {
            // delete / clean up some files before uninstallation
        })
        .WithFirstRun((v) => {
            MessageBox.Show("Thanks for installing my application!");
        })
        .Run(log);
}
```

The full list of options [for VelopackApp is available here](/sdk/Velopack.VelopackApp.html). You can also read more about [how hooks work](hooks.md).

> [!WARNING]
> A "FastCallback" requires that your application show no UI and exit quickly. When the callback returns, your application will exit. If you do not exit this callback quickly enough your process will be killed.

## Configuring Updates
Updates can be accomplished by adding [UpdateManager](/sdk/Velopack.UpdateManager.html) to your app:

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

> [!TIP]
> Updates can be done silently in the background, or integrated into your application UI. It's always up to you.

You can host your update packages basically anywhere, here are a few examples:
- Local directory:<br/>`new UpdateManager("C:\Updates")`
- HTTP server, or S3, Azure Storage, etc:<br/>`new UpdateManager("https://the.place/you-host/updates")`
- GitHub Releases:<br/>`new UpdateManager(new GitHubSource("https://github.com/yourName/yourRepo")`

There are a variety of [built-in sources](/sdk/Velopack.Sources.html) you can use when checking for updates, but you can also build your own by [deriving from IUpdateSource](/sdk/Velopack.Sources.IUpdateSource.html).

### Check for updates
`CheckForUpdatesAsync` will read the provided update source for a `releases.{channel}.json` file to retrieve available updates ([Read about channels](../packaging/channels.md)). If there is an update available, a non-null [UpdateInfo](/sdk/Velopack.UpdateInfo.html) will be returned with some details about the update. You can also [retrieve any release notes](release-notes.md) which were provided when the update was packaged.

There are [also some options](/sdk/Velopack.UpdateOptions.html) which can be passed in to [UpdateManager](/sdk/Velopack.UpdateManager.html) to customise how updates are handled, eg. to allow things like [switching channels](switching-channels.md).

### Download updates
`DownloadUpdatesAsync` will attempt to download deltas (if available) and re-construct the latest full release. If there are no deltas available, or the delta reconstruction fails, the latest full release package will be downloaded instead. Note that if an option like `AllowVersionDowngrade` is specified, the downloaded version might be older than the currently executing version.

### Apply updates
Once the update has downloaded, you have a few options available. Calling `ApplyUpdatesAndRestart` or `ApplyUpdatesAndExit` will exit your app, install any [bootstrap prerequisites](../packaging/bootstrapping.md), install the update, and then optionally restart your app right away.

If you do not want to exit your app immediately, you can call `WaitExitThenApplyUpdates` instead, which will launch Update.exe and wait for 60 seconds before proceeding. If your app has not exited within 60 seconds it will be killed.

Lastly, if you do not call any of these "Apply" methods, when you re-launch your app, by default, Velopack will detect that there is a pending update and install it then. If you wish to disable this, you should call `VelopackApp.Build().SetAutoApplyOnStartup(false)`.

> [!TIP]
> It is recommended that you use one of the functions which explicitly applies a package (eg. `ApplyUpdatesAndRestart`), and do not rely on the AutoApply behavior as a rule of thumb. The auto behavior will only apply a downloaded version if it is > the currently installed version, so will not work if trying to downgrade or switch channels, and if more than one instance of your process is running it could result in the update failing or those other processes being terminated.