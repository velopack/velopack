using Squirrel;
using Squirrel.Locators;

try {
    if (args.Length >= 1 && args[0].StartsWith("--squirrel")) {
        // squirrel hooks
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "..", "args.txt"), String.Join(" ", args) + Environment.NewLine);
        return 0;
    }

    if (args.Length == 1 && args[0] == "version") {
        var locator = SquirrelLocator.GetDefault(new ConsoleLogger());
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
            um.ApplyUpdatesAndExit();
            return 0;
        }
    }

} catch (Exception ex) {
    Console.WriteLine("exception: " + ex.ToString());
    return -1;
}

Console.WriteLine("Invalid args: " + String.Join(", ", args));
return -1;