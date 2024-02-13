*Applies to: Windows*

# Hooks
In general, I don't recommend trying to handle hooks manually - and instead refer to the available [VelopackApp](/sdk/Velopack.VelopackApp.html) options.

If you wish to handle these yourself, an SDK doesn't exist for your language, or you just want to learn more about it, read on.

## Command line hooks
At various stages of the install/update/uninstall process, Velopack will execute your main executable (the one specified when packaging with `--mainExe {exeName}`) with certain cli arguments and expect your app to exit as quickly as possible. 

- `--veloapp-install {version}` Occurs after the program has been extracted, but before the install has finished. App must handle and exit within 30 seconds.
- `--veloapp-obsolete {version}` Runs on the old version of the app, before an update is applied. App must handle and exit within 15 seconds.
- `--veloapp-updated {version}` Runs on the new version of the app, after an update is applied. App must handle and exit within 15 seconds.
- `--veloapp-uninstall {version}` Runs before an uninstall takes place. App must handle and exit within 30 seconds.

At this time, there is no way to provide feedback during the hooks that you would like to cancel the install/uninstall/update etc, and you may not show any UI to the user.

> [!WARNING]
> If your application receives one of these arguments and does not exit within the alloted time, it will be killed.

## Environment variable hooks
There are also two environment variables that get set, if these are detected your app does not need to exit.

- `VELOPACK_FIRSTRUN` is true if this is the first run after the app was installed.
- `VELOPACK_RESTART` is true if the application was restarted by Velopack (usually because an update was applied.)