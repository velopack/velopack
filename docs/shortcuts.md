| [docs](.) / shortcuts.md |
|:---|

# Windows Shortcuts
*Applies to: Windows*

By default, during installation Velopack will create a shortcut on the Desktop and in the StartMenuRoot. 

It will automatically delete any shortcuts it finds when uninstalling the application.

If you need to create shortcuts in any extra locations, the `Velopack.Windows.Shortcuts` and `Velopack.Windows.ShellLink` classes are provided. 

These classes are provided for legacy reasons, and in general the stability of such functions is not guarenteed.

For example, if you wished to create a shortcut during the install of your app, you might do the following:

```cs
using Velopack;
using Velopack.Windows;

VelopackApp.Build()
    .WithAfterInstallFastCallback((v) => new Shortcuts().CreateShortcutForThisExe(ShortcutLocation.Desktop))
    .Run()
```