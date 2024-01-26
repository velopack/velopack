| [docs](.) / migrating.md |
|:---|

# Migrating to Velopack
*Applies to: Windows*

## Squirrel.Windows and Clowd.Squirrel
If you are using one of these packages in your application, migrating will be mostly automated. Here are the general steps needed:

1. Replace the `Squirrel.Windows` or `Clowd.Squirrel` nuget package with the latest [`Velopack NuGet Package`](https://www.nuget.org/packages/velopack).

0. Install the `vpk` command line tool, as this is what you'll use to build Velopack releases.
   ```cmd
   dotnet tool install -g vpk
   ```

0. You will need to replace `SquirrelAwareApp` at the beginning of your app to `VelopackApp.Build().Run()`. Shortcuts [[Read more]](shortcuts.md) and registry entries are managed automatically for you in Velopack, so if you are currently doing this in `SquirrelAwareApp` hooks they should be removed. For example, if your hooks were this before:
   ```cs
   public static void Main(string[] args)
   {
       SquirrelAwareApp.HandleEvents(
           onInitialInstall: OnAppInstall,
           onAppUninstall: OnAppUninstall,
           onEveryRun: OnAppRun);
   }
   
   private static void OnAppInstall(SemanticVersion version, IAppTools tools)
   {
       tools.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
   }
   
   private static void OnAppUninstall(SemanticVersion version, IAppTools tools)
   {
       tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
   }
   
   private static void OnAppRun(SemanticVersion version, IAppTools tools, bool firstRun)
   {
       if (firstRun) MessageBox.Show("Thanks for installing my application!");
   }
   ```
   Then you would migrate to the following code, removing the shortcut hooks:
   ```cs
   public static void Main(string[] args)
   {
       VelopackApp.Build()
           .WithFirstRun(v => MessageBox.Show("Thanks for installing my application!"))
           .Run();
   }
   ```

0. The concept of `SquirrelAwareApp` no longer exists, so if you've added any attributes, assembly manifest entries, or other files to indicate that your binary is now aware, you can remove that. Every Velopack package has exactly one "VelopackApp" binary, which must implement the above interface at the top of `Main`. By default, Velopack will search for a binary in `{packDir}\{packId}.exe`. If your exe is named differently, you should provide the name with the `--mainExe yourApp.exe` argument.

0. The "RELEASES" file is no longer a format that Velopack uses, but it will produce one when building packages on windows with the default channel (eg. no channel argument provided). Instead, Velopack will produce `releases.{channel}.json` files, which should be treated in the same way. If you are wishing for a legacy windows app to migrate to Velopack, you should upload both the `RELEASES` file and the `releases.win.json` file which is produced by Velopack to your update feed.

0. In general, the command line supports all of the same features, but argument names or commands may have changed. Velopack no longer supports taking a `.nupkg` which was created by dotnet or nuget.exe. You should publish your app, and use `vpk pack` instead. A very simple example might look like this
   ```cmd
   dotnet publish --self-contined -r win-x64 -o publish
   vpk pack -u YourAppId -v 1.0.0 -p publish -e yourMainBinary.exe
   ```

   Please review the vpk command line help for more details:
   ```cmd
   vpk -h
   ```


## ClickOnce
There is no guide or advice for migrating ClickOnce applications yet. If you would like to contribute one, please open an issue or PR!