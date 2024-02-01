#pragma warning disable CA1416 // Validate platform compatibility
using System.Diagnostics;
using Velopack;
using Velopack.Locators;

try {
    bool shouldExit = false;
    bool shouldAutoUpdate = args.Any(a => a.Equals("--autoupdate", StringComparison.OrdinalIgnoreCase));

#if !NO_VELO_BUILDER
    VelopackApp.Build()
        .SetAutoApplyOnStartup(shouldAutoUpdate)
        .WithFirstRun((v) => {
            debugFile("firstrun", v.ToString());
            Console.WriteLine("was first run");
            shouldExit = true;
        })
        .WithRestarted((v) => {
            debugFile("restarted", v.ToString() + "," + String.Join(",", args));
            Console.WriteLine("app just restarted");
            shouldExit = true;
        })
        .WithAfterInstallFastCallback((v) => debugFile("args.txt", String.Join(" ", args)))
        .WithBeforeUpdateFastCallback((v) => debugFile("args.txt", String.Join(" ", args)))
        .WithAfterUpdateFastCallback((v) => debugFile("args.txt", String.Join(" ", args)))
        .WithBeforeUninstallFastCallback((v) => debugFile("args.txt", String.Join(" ", args)))
        .Run(new ConsoleLogger());

    if (shouldAutoUpdate) {
        // this shouldn't be reached
        return -1;
    }

    if (shouldExit) {
        return 0;
    }
#endif

    if (args.Length == 1 && args[0] == "version") {
        var locator = VelopackLocator.GetDefault(new ConsoleLogger());
        Console.WriteLine(locator.CurrentlyInstalledVersion?.ToString() ?? "unknown_version");
        return 0;
    }

    if (args.Length == 1 && args[0] == "test") {
        Console.WriteLine(Const.TEST_STRING ?? "no_test_string");
        return 0;
    }

    if (args.Length == 2) {
        if (args[0] == "check") {
            var um = new UpdateManager(args[1], null, new ConsoleLogger());
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
            var um = new UpdateManager(args[1], null, new ConsoleLogger());
            var info = um.CheckForUpdates();
            if (info == null) {
                Console.WriteLine("no updates");
                return -1;
            }
            um.DownloadUpdates(info, (x) => Console.WriteLine(x));
            return 0;
        }

        if (args[0] == "apply") {
            var um = new UpdateManager(args[1], null, new ConsoleLogger());
            if (!um.IsUpdatePendingRestart) {
                Console.WriteLine("not pending restart");
                return -1;
            }
            Console.WriteLine("applying...");
            um.ApplyUpdatesAndRestart((VelopackAsset) null, new[] { "test", "args !!" });
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

void debugFile(string name, string message)
{
    var path = Path.Combine(AppContext.BaseDirectory, "..", name);
    File.AppendAllText(path, message + Environment.NewLine);
}
