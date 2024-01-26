*Applies to: Windows, MacOS, Linux*
# Packaging Overview

Packaging a release is accomplished with the `pack` command in Velopack. Regardless of your operating system, the common required arguments are roughly the same. 

## Creating your first release
You first should compile your application with whatever toolchain you would normally use (eg. `dotnet publish`, `msbuild.exe`, so forth). 
Henceforth this will be called `{build_dir}`.

### Required arguments
- `--packId {id}` The unique ID of your application. This should be unique enough to avoid other application authors from colliding with your app.
- `--packVersion {version}` The current version you are releasing - in [semver2 format](https://semver.org/) (eg. `1.0.0-build.23+metadata`). 
- `--packDir {build_dir}` The folder containing your compiled application.
- `--mainExe {exeName}` The main executable to be started after install, and the binary that will [handle Velopack Hooks](../updating/overview.md).
- `--icon {path}` The icon used to bundle your app. Only required on MacOS and Linux.

> [!TIP]
> Velopack does not support 4 part versions (eg. `1.0.0.0`), as it would not be practical to support both formats simultaneously and semver2 offers a lot more flexibility.

A complete example:
```cmd
dotnet publish -c Release -r win-x64 -o publish
vpk pack --packId MyAppId -packVersion 1.0.0 --packDir publish --mainExe MyApp.exe
```

### Optional recommended arguments
There are many optional arguments, the best way to see what features are available for your operating system is to check `vpk pack -h`. To mention a couple:
- `--packTitle {name}` The friendly name for your app, shown to users in dialogs, shortcuts, etc.
- `--outputDir {path}` The location Velopack should create the final releases (defaults to `.\Releases`)

### Release output
When building a release has completed, you should have the following assets in your `--outputDir`:
- `MyAppId-1.0.0-full.nupkg` - Full Release: contains your entire update package.
- `MyAppId-1.0.0-delta.nupkg` - Delta Release: only if there was a previous release to build a delta from. These are optional to build/deploy, but speeds up the updating process for sers because they only need to download what's changed between versions instead of the full package.
- `MyAppId-Portable.zip` - Portable Release: Can deploy this optionally to allow users to run and update your app without installing.
- `MyAppId-Setup.exe` - Installer: Used by most users to install the app to the local filesystem.
- `releases.{channel}.json` - Releases Index: a list of every available release. Used by `UpdateManager` to locate the latest applicable release.
- `RELEASES` - Legacy Releases File: only used for clients [migrating to Velopack](../migrating.md) from Squirrel.
- `assets.{channel}.json` - Build Assets: A list of assets created in the most recent build. Used by [Velopack deployment commands](../distributing/overview.md).

You do not need to deploy all of these files to allow users to update, so you should review the [deployment guide](../distributing/overview.md) for more information on which files to distribute.

> [!TIP]
> There is no setup/installer package for Linux. The program is distributed as a self-updating `.AppImage`. The reason is that `.AppImage` will run on pretty much every modern distro with no extra dependencies needed. Just download the `.AppImage`, run `chmod +x`, and click it to start. It is possible to install an `.AppImage`, but this is left up to the user to install something like [appimaged](https://github.com/probonopd/go-appimage/blob/master/src/appimaged/README.md) or [AppImageLauncher](https://github.com/TheAssassin/AppImageLauncher).

## Code signing
While this is not required for local builds / testing, you should always code-sign your application before distributing your application to users. 

> [!WARNING]
> If you do not code-sign, your application may fail to run. [[Read more]](signing.md)

## Customising the installer
On platforms which ship installers, you can customise the behavior. [[Read more]](installer.md)

## Other recommended arguments
- If your application is operating-system or CPU architecture specific you should consider adding an `--rid`. [[Read more]](rid.md)
- If you plan on distributing release channels for different architectures or features, consider adding a `--channel` [[Read more]](channels.md)
- If your app requires additional frameworks (eg. vcredist) consider `--framework` [[Read more]](bootstrapping.md)