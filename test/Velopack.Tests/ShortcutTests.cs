#pragma warning disable CS0618 // Type or member is obsolete
using System.Runtime.Versioning;
using Velopack.Core;
using Velopack.Locators;
using Velopack.Util;
using Velopack.Windows;

namespace Velopack.Tests;

public class ShortcutTests
{
    private readonly ITestOutputHelper _output;

    public ShortcutTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void CanCreateAndRemoveShortcuts()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = _output.BuildLoggerFor<ShortcutTests>();
        string exeName = "NotSquirrelAwareApp.exe";

        using var _1 = TempUtil.GetTempDirectory(out var rootDir);
        var packages = Directory.CreateDirectory(Path.Combine(rootDir, "packages"));
        var current = Directory.CreateDirectory(Path.Combine(rootDir, "current"));

        PathHelper.CopyFixtureTo("AvaloniaCrossPlat-1.0.15-win-full.nupkg", packages.FullName);
        PathHelper.CopyFixtureTo(exeName, current.FullName);

        var locator = new TestVelopackLocator(
            "AvaloniaCrossPlat",
            "1.0.0",
            packages.FullName,
            current.FullName,
            rootDir,
            null,
            null,
            logger.ToVelopackLogger());
        var sh = new Shortcuts(locator);
        var flag = ShortcutLocation.StartMenuRoot | ShortcutLocation.Desktop;
        sh.DeleteShortcuts(exeName, flag);
        sh.CreateShortcut(exeName, flag, false, "");
        var shortcuts = sh.FindShortcuts(exeName, flag);
        Assert.Equal(2, shortcuts.Keys.Count);

        var startDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        var desktopDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        var lnkName = "SquirrelAwareApp.lnk";

        var start = shortcuts[ShortcutLocation.StartMenuRoot];
        var desktop = shortcuts[ShortcutLocation.Desktop];

        var target = Path.Combine(current.FullName, exeName);
        Assert.Equal(Path.Combine(startDir, lnkName), start.ShortCutFile);
        Assert.Equal(target, start.Target);
        Assert.Equal(Path.Combine(desktopDir, lnkName), desktop.ShortCutFile);
        Assert.Equal(target, desktop.Target);

        sh.DeleteShortcuts(exeName, flag);
        var after = sh.FindShortcuts(exeName, flag);
        Assert.Equal(0, after.Keys.Count);
    }
}