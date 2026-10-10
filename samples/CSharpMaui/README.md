# CSharpMaui
_Prerequisites: vpk command line tool installed, and the .NET MAUI workload (`dotnet workload install maui-windows` on Windows, `dotnet workload install maui-maccatalyst` plus Xcode on macOS)_

This app demonstrates how to use .NET MAUI to provide a desktop UI, installer, and updates for Windows and macOS (via Mac Catalyst).

You can run this sample by executing the build script with a version number (eg. `build.bat 1.0.0` on Windows, `./build.sh 1.0.0` on macOS).

Once built, you can install the app - build more updates, and then test updates and so forth. The sample app will check the local release dir for new update packages.

In your production apps, you should deploy your updates to some kind of update server instead.

.NET MAUI does not support Linux, so there is no Linux build of this sample.

## MAUI Implementation Notes
MAUI has a separate entry point for each platform, and `VelopackApp.Build().Run()` should be called at the top of each one, before any MAUI or platform UI code runs:

- Mac Catalyst: `Main()` in `Platforms/MacCatalyst/Program.cs`, before `UIApplication.Main()`.
- Windows: WinUI normally generates `Main()` for you, so the sample defines `DISABLE_XAML_GENERATED_MAIN` in the csproj and provides its own `Main()` in `Platforms/Windows/Program.cs`. This lets Velopack handle its install and update hooks before the XAML runtime starts.

### Windows
The app is built unpackaged (`WindowsPackageType=None`) so it is not an MSIX, and the Windows App SDK runtime is bundled with the app (`WindowsAppSDKSelfContained=true`) so users do not need to install it separately. The .NET runtime is not bundled; the Velopack installer installs it if needed (`vpk pack -f net10-x64-desktop`).

### macOS (Mac Catalyst)
The `.app` bundle produced by the `net10.0-maccatalyst` target is passed directly to `vpk pack`.

Velopack updates the app by replacing its `.app` bundle from outside the process, so the App Sandbox must be disabled. This means Velopack apps cannot be distributed through the Mac App Store. The MAUI template ships a `Platforms/MacCatalyst/Entitlements.plist` which enables the sandbox; this sample deletes it. When you code-sign with `vpk pack --signAppIdentity`, Velopack re-signs the bundle with its own entitlements (no sandbox, or whatever you provide with `--signEntitlements`), but unsigned builds are left exactly as MAUI signed them, so make sure your own entitlements do not include `com.apple.security.app-sandbox`.
