# Uno with Velopack

This solution contains an Uno Desktop application that targets both Windows and macOS. Because Velopack does not currently support mobile or web platforms, those are not included.

## Prerequisites
This solution will publish the application out to the Velopack Flow service. To test the full publish you will need to create a [Velopack Flow account](https://app.velopack.io).

The solution assumes that you have downloaded and installed the Velopack CLI (`vpk`) global tool. This tool can be installed by running `dotnet tool install -g vpk`.
You will need to authenticate with the Velopack Flow service by running `vpk login` and signing in with your Velopack Flow account.

## The Application
`Presentation/MainPage.xaml` contains the main UI for viewing the current version, checking for updates, and applying the latest update.
The `MainViewModel.cs` contains the logic for checking for updates and applying them. The key piece of the setup is the initialization of the `UpdateManager` with the `VelopackFlowUpdateSource` which provides the interaction with the Velopack Flow service.

The `App.xaml.cs` has been updated to contain the [Velopack application startup hook](https://docs.velopack.io/integrating/overview).

### Updating the Project file
There are two properties to set in the `csproj` file to enable the Velopack Flow integration. These are:
```xml
<VelopackPushOnPublish>true</VelopackPushOnPublish>
<VelopackPackId>Velopack.UnoSample</VelopackPackId>
```

`VelopackPackId` is the unique identifier for the application. This is used to identify the application in the Velopack Flow service. It must be unique among all applications.You **MUST** change this to be your own application identifier.
`VelopackPushOnPublish` is a boolean value that determines if the application should be pushed to the Velopack Flow service when it is published. This should be set to `true` to enable the integration.

### Building installers
To build the local installers run the following command replacing `<Version>` with the desired version number.
The `-f` option is used to specify the target framework. Because each target platform should result in a different build, you likely will want to specify a different Velopack channel for each target platform. By default, it will default to your target OS. You can specify the channel by adding `-p:VelopackChannel=<Channel>` to the command.

This can be any of the desktop frameworks specified in the `csproj` file.
```bash
dotnet publish -c Release -f net8.0-desktop -p:VelopackChannel=<Channel> -p:Version=<Version>
```

For example:
```bash
dotnet publish -c Release -f net8.0-desktop -p:VelopackChannel=win-desktop -p:Version=1.0.5
```
