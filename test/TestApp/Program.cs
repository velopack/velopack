#pragma warning disable CA1416 // Validate platform compatibility
using System.Diagnostics;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;

var locator = VelopackLocator.CreateDefaultForPlatform(logger: new ConsoleVelopackLogger());

try {
    bool shouldExit = false;
    bool shouldAutoUpdate = args.Any(a => a.Equals("--autoupdate", StringComparison.OrdinalIgnoreCase));

#if USE_ASYNC_MAIN
    await Task.Delay(10).ConfigureAwait(false);
#endif

#if !NO_VELO_BUILDER
    VelopackApp.Build()
        .SetAutoApplyOnStartup(shouldAutoUpdate)
        .OnFirstRun(
            (v) => {
                debugFile("firstrun", v.ToString(), "OnFirstRun");
                Console.WriteLine("was first run");
                shouldExit = true;
            })
        .OnRestarted(
            (v) => {
                debugFile("restarted", v.ToString() + "," + String.Join(",", args), "OnRestarted");
                Console.WriteLine("app just restarted");
                shouldExit = true;
            })
        .SetLocator(locator)
        .OnAfterInstallFastCallback((v) => debugFile("args.txt", String.Join(" ", args), "OnAfterInstallFastCallback"))
        .OnBeforeUpdateFastCallback((v) => debugFile("args.txt", String.Join(" ", args), "OnBeforeUpdateFastCallback"))
        .OnAfterUpdateFastCallback((v) => debugFile("args.txt", String.Join(" ", args), "OnAfterUpdateFastCallback"))
        .OnBeforeUninstallFastCallback((v) => debugFile("args.txt", String.Join(" ", args), "OnBeforeUninstallFastCallback"))
        .Run();

    if (shouldAutoUpdate) {
        // this shouldn't be reached
        return -1;
    }

    if (shouldExit) {
        return 0;
    }
#endif

    if (args.Length == 1 && args[0] == "version") {
        Console.WriteLine(locator.CurrentlyInstalledVersion?.ToString() ?? "unknown_version");
        return 0;
    }

    if (args.Length == 1 && args[0] == "test") {
        Console.WriteLine(Const.TEST_STRING ?? "no_test_string");
        return 0;
    }

    if (args.Length == 1 && args[0] == "packagesdir") {
        Console.WriteLine(locator.PackagesDir ?? "no_packages_dir");
        return 0;
    }

    if (args.Length == 2) {
        if (args[0] == "check") {
            var um = new UpdateManager(args[1], null, locator);
            var info = um.CheckForUpdates();
            if (info == null) {
                Console.WriteLine("no updates");
                return 0;
            } else {
                Console.WriteLine("update: " + info.TargetFullRelease.Version);
                return 0;
            }
        }

        if (args[0] == "download") {
            var um = new UpdateManager(args[1], null, locator);
            var info = um.CheckForUpdates();
            if (info == null) {
                Console.WriteLine("no updates");
                return -1;
            }

            um.DownloadUpdates(info, Console.WriteLine);
            return 0;
        }

        if (args[0] == "apply") {
            var um = new UpdateManager(args[1], null, locator);
            if (um.UpdatePendingRestart == null) {
                Console.WriteLine("not pending restart");
                return -1;
            }

            Console.WriteLine("applying...");
            um.ApplyUpdatesAndRestart(null, ["test", "args !!"]);
            return 0;
        }
    }
} catch (Exception ex) {
    Console.WriteLine("exception: " + ex.ToString());
    if (Debugger.IsAttached) throw;
    return -1;
}

Console.WriteLine("Invalid args: " + String.Join(", ", args));
return -1;

static void debugFile(string name, string message, string hook)
{
    var path = Path.Combine(AppContext.BaseDirectory, "..", name);
    File.AppendAllText(path, $"{hook}: {message}{Environment.NewLine}");
}