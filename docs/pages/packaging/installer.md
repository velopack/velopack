*Applies to: Windows, MacOS*

# Installer Overview
Velopack takes a relatively light-touch when it comes to installers, so there is not a lot of customisation available like you would find in other installation frameworks. This is the tradeoff Velopack makes to ensure that the developer/user experience is as fast and easy as possible.

In both operating systems, if [code signing is configured](signing.md) the installer will also be signed. (This is _required_ on MacOS)

## Windows Overview
The Windows installer is currently a "one-click" installer, meaning when the `Setup.exe` binary is run, Velopack will not show any questions / wizards to the user, it will simply attempt to install the app as fast as possible and then launch it. 

The setup will install a shortcut to `StartMenuRoot` and `Desktop` by default. [[Read more]](../updating/shortcuts.md)

The key options which will customize the installer are as follows:
- `--packTitle {app name}` customizes shortcut names, the Apps & Features name, and the portable entry exe name.
- `--icon {path}` sets the .ico on Update.exe and Setup.exe (and also the icon of any dialogs shown)
- `--splashImage {path}` sets the (possibly animated) splash image to be shown while installing.

The splash image can be a `jpeg`, `png`, or `gif`. In the latter case, it will be animated.

You can also [bootstrap required frameworks](bootstrapping.md) before installing your app.

The Windows installer will extract the application to `%LocalAppData%\{packId}`, and the directory structure will look like:

```
{packId}
├── current
│   ├── YourFile.dll
│   ├── sq.version
│   └── YourApp.exe
└── Update.exe
```

The `current` directory will be fully replaced [while doing updates](../updating/overview.md). The other two files added by Velopack (`Update.exe` and `sq.version`) are crucial and are required files for Velopack to be able to properly update your application.

## MacOS Overview
The MacOS installer will be a standard `.pkg` - which is just a bundle where the UI is provided by the operating system, allowing the user to pick the install location. The app will be launched automatically after the install (mirroring the behavior on Windows) because of a `postinstall` script added by Velopack.

The key options which will customize the installer are as follows:
- `--packTitle {app name}` customizes the name of the `.app` bundle and the app name shown in the `.pkg`
- `--pkgWelcome {path}` adds a Welcome page
- `--pkgReadme {path}` adds a Readme page
- `--pkgLicense {path}` adds a License Acceptance page
- `--pkgConclusion {path}` adds a Conclusion page
- `--noPkg` disable generating a `.pkg` installer entirely

The pkgPage arguments can be a `.rtf` or a `.html` file.

The `.app` package can be extracted to `/Applications` or `~/Applications`, this is selected by the user while installing.