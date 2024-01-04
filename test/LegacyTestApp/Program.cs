using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Squirrel;
using Squirrel.SimpleSplat;

[assembly: AssemblyMetadata("SquirrelAwareVersion", "1")]

namespace LegacyTestApp
{
    internal class Program
    {
        static int Main(string[] args)
        {
#if CLOWD
            SquirrelAwareApp.HandleEvents(
                 onInitialInstall: (v, t) => debugFile("args.txt", String.Join(" ", args)),
                 onAppUpdate: (v, t) => debugFile("args.txt", String.Join(" ", args)),
                 onAppUninstall: (v, t) => debugFile("args.txt", String.Join(" ", args)),
                 onEveryRun: (v, t, f) => debugFile("args.txt", String.Join(" ", args))
            );
#else
            SquirrelAwareApp.HandleEvents(
                 onInitialInstall: v => debugFile("args.txt", String.Join(" ", args)),
                 onAppUpdate: v => debugFile("args.txt", String.Join(" ", args)),
                 onAppUninstall: v => debugFile("args.txt", String.Join(" ", args)),
                 onFirstRun: () => debugFile("args.txt", String.Join(" ", args))
            );
#endif

            try {

                SquirrelLogger.Register();

                if (args.Length == 1 && args[0] == "version") {
                    using var um = new UpdateManager("");
                    Console.WriteLine(um.CurrentlyInstalledVersion()?.ToString() ?? "unknown_version");
                    return 0;
                }

                if (args.Length == 2) {
                    if (args[0] == "check") {
                        using var um = new UpdateManager(args[1]);
                        var info = um.CheckForUpdate().GetAwaiter().GetResult();
                        if (info == null || info.ReleasesToApply == null || info.FutureReleaseEntry == null || info.ReleasesToApply.Count == 0) {
                            Console.WriteLine("no updates");
                            return 0;
                        } else {
                            Console.WriteLine("update: " + info.FutureReleaseEntry.Version);
                            return 0;
                        }
                    }

                    if (args[0] == "download") {
                        using var um = new UpdateManager(args[1]);
                        var entry = um.UpdateApp().GetAwaiter().GetResult();
                        return entry == null ? -1 : 0;
                    }

                    if (args[0] == "apply") {
                        UpdateManager.RestartApp();
                        return 0;
                    }
                }

            } catch (Exception ex) {
                Console.WriteLine("exception: " + ex.ToString());
                if (Debugger.IsAttached) throw;
                return -1;
            }

            Console.WriteLine("Unhandled args: " + String.Join(", ", args));
            return -1;
        }

        static void debugFile(string name, string message)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "..", name);
            File.AppendAllText(path, message + Environment.NewLine);
        }
    }
}
