using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using Velopack.Locators;

namespace Velopack
{
    /// <summary>
    /// A delegate type for handling Velopack startup events
    /// </summary>
    /// <param name="version">The currently executing version of this application</param>
    public delegate void VelopackHook(SemanticVersion version);

    /// <summary>
    /// VelopackApp helps you to handle app activation events correctly.
    /// This should be used as early as possible in your application startup code.
    /// (eg. the beginning of Main() in Program.cs)
    /// </summary>
    public sealed class VelopackApp
    {
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        internal static ILogger DefaultLogger { get; private set; } = NullLogger.Instance;

        internal static IVelopackLocator? DefaultLocator { get; private set; }

        VelopackHook? _install;
        VelopackHook? _update;
        VelopackHook? _obsolete;
        VelopackHook? _uninstall;
        VelopackHook? _firstrun;
        VelopackHook? _restarted;
        string[]? _args;
        bool _autoApply = true;

        private VelopackApp()
        {
        }

        /// <summary>
        /// Creates and returns a new Velopack application builder.
        /// </summary>
        public static VelopackApp Build() => new VelopackApp();

        /// <summary>
        /// Override the command line arguments used to determine the Velopack hook to run.
        /// If this is not set, the command line arguments passed to the application will be used.
        /// </summary>
        public VelopackApp SetArgs(string[] args)
        {
            _args = args;
            return this;
        }

        /// <summary>
        /// Set whether to automatically apply downloaded updates on startup. This is ON by default.
        /// </summary>
        public VelopackApp SetAutoApplyOnStartup(bool autoApply)
        {
            _autoApply = autoApply;
            return this;
        }

        /// <summary>
        /// Override the default <see cref="IVelopackLocator"/> used to search for application paths.
        /// This will be cached and potentially re-used throughout the lifetime of the application.
        /// </summary>
        public VelopackApp SetLocator(IVelopackLocator locator)
        {
            DefaultLocator = locator;
            return this;
        }

        /// <summary>
        /// This hook is triggered when the application is started for the first time after installation.
        /// </summary>
        public VelopackApp WithFirstRun(VelopackHook hook)
        {
            _firstrun += hook;
            return this;
        }

        /// <summary>
        /// This hook is triggered when the application is restarted by Velopack after installing updates.
        /// </summary>
        public VelopackApp WithRestarted(VelopackHook hook)
        {
            _restarted += hook;
            return this;
        }

        /// <summary>
        /// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
        /// Your code will be run and then <see cref="Environment.Exit(int)"/> will be called.
        /// If your code has not completed within 30 seconds, it will be terminated.
        /// Only supported on windows; On other operating systems, this will never be called.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public VelopackApp WithAfterInstallFastCallback(VelopackHook hook)
        {
            _install += hook;
            return this;
        }

        /// <summary>
        /// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
        /// Your code will be run and then <see cref="Environment.Exit(int)"/> will be called.
        /// If your code has not completed within 15 seconds, it will be terminated.
        /// Only supported on windows; On other operating systems, this will never be called.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public VelopackApp WithAfterUpdateFastCallback(VelopackHook hook)
        {
            _update += hook;
            return this;
        }

        /// <summary>
        /// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
        /// Your code will be run and then <see cref="Environment.Exit(int)"/> will be called.
        /// If your code has not completed within 15 seconds, it will be terminated.
        /// Only supported on windows; On other operating systems, this will never be called.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public VelopackApp WithBeforeUpdateFastCallback(VelopackHook hook)
        {
            _obsolete += hook;
            return this;
        }

        /// <summary>
        /// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
        /// Your code will be run and then <see cref="Environment.Exit(int)"/> will be called.
        /// If your code has not completed within 30 seconds, it will be terminated.
        /// Only supported on windows; On other operating systems, this will never be called.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public VelopackApp WithBeforeUninstallFastCallback(VelopackHook hook)
        {
            _uninstall += hook;
            return this;
        }

        /// <summary>
        /// Runs the Velopack application startup code and triggers any configured hooks.
        /// </summary>
        /// <param name="logger">A logging interface for diagnostic messages. This will be
        /// cached and potentially re-used throughout the lifetime of the application.</param>
        public void Run(ILogger? logger = null)
        {
            var args = _args ?? Environment.GetCommandLineArgs().Skip(1).ToArray();

            // internal hook run by the Velopack tooling to check everything is working
            if (args.Length >= 1 && args[0].Equals("--veloapp-version", StringComparison.OrdinalIgnoreCase)) {
                Console.WriteLine(VelopackRuntimeInfo.VelopackNugetVersion);
                Exit(0);
                return;
            }

            var log = logger ?? NullLogger.Instance;
            var locator = DefaultLocator ?? VelopackLocator.GetDefault(log);
            DefaultLogger = log;

            log.Info("Starting Velopack App (Run).");

            if (VelopackRuntimeInfo.IsWindows && locator.AppId != null) {
                var appUserModelId = Utility.GetAppUserModelId(locator.AppId);
                log.Info($"Setting current process explicit AppUserModelID to '{appUserModelId}'");
                SetCurrentProcessExplicitAppUserModelID(appUserModelId);
            }

            // first, we run any fast exit hooks
            VelopackHook defaultBlock = ((v) => { });
            var fastExitlookup = new[] {
                new { Key = "--veloapp-install", Value = _install ?? defaultBlock },
                new { Key = "--veloapp-updated", Value = _update ?? defaultBlock },
                new { Key = "--veloapp-obsolete", Value = _obsolete ?? defaultBlock },
                new { Key = "--veloapp-uninstall", Value = _uninstall ?? defaultBlock },
                // ignore the legacy hooks
                new { Key = "--squirrel-install", Value = defaultBlock },
                new { Key = "--squirrel-updated", Value = defaultBlock },
                new { Key = "--squirrel-obsolete", Value = defaultBlock },
                new { Key = "--squirrel-uninstall", Value = defaultBlock },
            }.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
            if (args.Length >= 2 && fastExitlookup.ContainsKey(args[0])) {
                try {
                    log.Info("Found fast exit hook: " + args[0]);
                    var version = SemanticVersion.Parse(args[1]);
                    fastExitlookup[args[0]](version);
                    log.Info("Completed hook, exiting...");
                    Exit(0);
                    return;
                } catch (Exception ex) {
                    log.Error(ex, $"Error occurred executing user defined Velopack hook. ({args[0]})");
                    Exit(-1);
                    return;
                }
            }

            // some initial setup/state
            var myVersion = locator.CurrentlyInstalledVersion;
            if (myVersion == null) {
                return;
            }

            var firstrun = !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_FIRSTRUN"));
            var restarted = !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_RESTART"));
            var localPackages = locator.GetLocalPackages();
            var latestLocal = locator.GetLatestLocalFullPackage();

            Environment.SetEnvironmentVariable("VELOPACK_FIRSTRUN", null);
            Environment.SetEnvironmentVariable("VELOPACK_RESTART", null);

            // if we've not just been restarted via Velopack apply, and there is a local update available,
            // we should install it first.
            if (latestLocal != null && latestLocal.Version > myVersion) {
                log.Info($"Launching app is out-dated. Current: {myVersion}, Newest Local Available: {latestLocal.Version}");
                if (!restarted && _autoApply) {
                    log.Info("Auto apply is true, so restarting to apply update...");
                    UpdateExe.Apply(locator, latestLocal, false, true, args, log);
                    Exit(0);
                } else {
                    log.Info("Pre-condition failed, we will not restart to apply updates. (restarted: " + restarted + ", autoApply: " + _autoApply + ")");
                }
            }

            // clean up old versions of the app
            var pkgPath = locator.PackagesDir;
            if (pkgPath != null) {
                foreach (var package in localPackages) {
                    if (package.Type == VelopackAssetType.Full && (package.Version == latestLocal?.Version || package.Version == myVersion)) {
                        continue;
                    }

                    try {
                        log.Info("Removing old package: " + package.FileName);
                        var p = Path.Combine(pkgPath, package.FileName);
                        File.Delete(p);
                    } catch (Exception ex) {
                        log.Error(ex, $"Failed to remove old package '{package.FileName}'");
                    }
                }
            }

            // run non-exiting user hooks
            if (firstrun) {
                try {
                    _firstrun?.Invoke(myVersion);
                } catch (Exception ex) {
                    log.Error(ex, $"Error occurred executing user defined Velopack hook. (firstrun)");
                }
            }

            if (restarted) {
                try {
                    _restarted?.Invoke(myVersion);
                } catch (Exception ex) {
                    log.Error(ex, $"Error occurred executing user defined Velopack hook. (restarted)");
                }
            }
        }

        private void Exit(int code)
        {
            if (!VelopackRuntimeInfo.InUnitTestRunner) {
                Environment.Exit(code);
            }
        }
    }
}