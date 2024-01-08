using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Velopack.Locators;
using Velopack.Windows;

namespace Velopack.Tests
{
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

            using var _1 = Utility.GetTempDirectory(out var pkgDir);
            using var _2 = Utility.GetTempDirectory(out var appDir);
            PathHelper.CopyFixtureTo("AvaloniaCrossPlat-1.0.15-win-full.nupkg", pkgDir);
            PathHelper.CopyFixtureTo(exeName, appDir);

            var locator = new TestVelopackLocator("AvaloniaCrossPlat", "1.0.0", pkgDir, appDir, appDir, null, logger);
            var sh = new Shortcuts(logger, locator);
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

            Assert.Equal(Path.Combine(startDir, lnkName), start.ShortCutFile);
            Assert.Equal(Path.Combine(desktopDir, lnkName), desktop.ShortCutFile);

            sh.DeleteShortcuts(exeName, flag);
            var after = sh.FindShortcuts(exeName, flag);
            Assert.Equal(0, after.Keys.Count);
        }
    }
}
