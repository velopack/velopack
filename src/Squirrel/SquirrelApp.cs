using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using Squirrel.Locators;

namespace Squirrel
{
    /// <summary>
    /// A delegate type for handling Squirrel startup events
    /// </summary>
    /// <param name="version">The currently executing version of this application</param>
    public delegate void SquirrelHook(SemanticVersion version);

    /// <summary>
    /// SquirrelApp helps you to handle Squirrel app activation events correctly.
    /// This should be used as early as possible in your application startup code.
    /// (eg. the beginning of Main() in Program.cs)
    /// </summary>
    public sealed class SquirrelApp
    {
        ISquirrelLocator _locator;
        SquirrelHook _install;
        SquirrelHook _update;
        SquirrelHook _obsolete;
        SquirrelHook _uninstall;
        SquirrelHook _firstrun;
        SquirrelHook _restarted;
        string[] _args;
        bool _autoApply = true;

        private SquirrelApp()
        {
        }

        /// <summary>
        /// Creates and returns a new Squirrel application builder.
        /// </summary>
        public static SquirrelApp Build() => new SquirrelApp();

        /// <summary>
        /// Override the command line arguments used to determine the Squirrel hook to run.
        /// If this is not set, the command line arguments passed to the application will be used.
        /// </summary>
        public SquirrelApp SetArgs(string[] args)
        {
            _args = args;
            return this;
        }

        /// <summary>
        /// Set whether to automatically apply downloaded updates on startup. This is ON by default.
        /// </summary>
        public SquirrelApp SetAutoApplyOnStartup(bool autoApply)
        {
            _autoApply = autoApply;
            return this;
        }

        /// <summary>
        /// Override the default <see cref="ISquirrelLocator"/> used to search for application paths.
        /// </summary>
        public SquirrelApp SetLocator(ISquirrelLocator locator)
        {
            _locator = locator;
            return this;
        }

        /// <summary>
        /// This hook is triggered when the application is started for the first time after installation.
        /// </summary>
        public SquirrelApp WithFirstRun(SquirrelHook hook)
        {
            _firstrun = hook;
            return this;
        }

        /// <summary>
        /// This hook is triggered when the application is restarted by Squirrel after installing updates.
        /// </summary>
        public SquirrelApp WithRestarted(SquirrelHook hook)
        {
            _restarted = hook;
            return this;
        }

        /// <summary>
        /// WARNING: FastCallback hooks are run during critical stages of Squirrel operations.
        /// Your code will be run and then <see cref="Environment.Exit(int)"/> will be called.
        /// If your code has not completed within 30 seconds, it will be terminated.
        /// Only supported on windows; On other operating systems, this will never be called.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public SquirrelApp WithAfterInstallFastCallback(SquirrelHook hook)
        {
            _install = hook;
            return this;
        }

        /// <summary>
        /// WARNING: FastCallback hooks are run during critical stages of Squirrel operations.
        /// Your code will be run and then <see cref="Environment.Exit(int)"/> will be called.
        /// If your code has not completed within 15 seconds, it will be terminated.
        /// Only supported on windows; On other operating systems, this will never be called.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public SquirrelApp WithAfterUpdateFastCallback(SquirrelHook hook)
        {
            _update = hook;
            return this;
        }

        /// <summary>
        /// WARNING: FastCallback hooks are run during critical stages of Squirrel operations.
        /// Your code will be run and then <see cref="Environment.Exit(int)"/> will be called.
        /// If your code has not completed within 15 seconds, it will be terminated.
        /// Only supported on windows; On other operating systems, this will never be called.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public SquirrelApp WithBeforeUpdateFastCallback(SquirrelHook hook)
        {
            _obsolete = hook;
            return this;
        }

        /// <summary>
        /// WARNING: FastCallback hooks are run during critical stages of Squirrel operations.
        /// Your code will be run and then <see cref="Environment.Exit(int)"/> will be called.
        /// If your code has not completed within 30 seconds, it will be terminated.
        /// Only supported on windows; On other operating systems, this will never be called.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public SquirrelApp WithBeforeUninstallFastCallback(SquirrelHook hook)
        {
            _uninstall = hook;
            return this;
        }

        /// <summary>
        /// Runs the Squirrel application startup code and triggers any configured hooks.
        /// </summary>
        /// <param name="logger">A logging interface for diagnostic messages.</param>
        public void Run(ILogger logger = null)
        {
            var args = _args ?? Environment.GetCommandLineArgs().Skip(1).ToArray();
            var log = logger ?? NullLogger.Instance;
            var locator = _locator ?? SquirrelLocator.GetDefault(log);

            // internal hook run by the Squirrel tooling to check everything is working
            if (args.Length >= 1 && args[0].Equals("--squirrel-version", StringComparison.OrdinalIgnoreCase)) {
                Console.WriteLine(SquirrelRuntimeInfo.SquirrelNugetVersion);
                Environment.Exit(0);
            }

            log.Info("Starting Squirrel App (Run).");

            // first, we run any fast exit hooks
            SquirrelHook defaultBlock = ((v) => { });
            var fastExitlookup = new[] {
                new { Key = "--squirrel-install", Value = _install ?? defaultBlock },
                new { Key = "--squirrel-updated", Value = _update ?? defaultBlock },
                new { Key = "--squirrel-obsolete", Value = _obsolete ?? defaultBlock },
                new { Key = "--squirrel-uninstall", Value = _uninstall ?? defaultBlock },
            }.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
            if (args.Length >= 2 && fastExitlookup.ContainsKey(args[0])) {
                try {
                    log.Info("Found fast exit hook: " + args[0]);
                    var version = SemanticVersion.Parse(args[1]);
                    fastExitlookup[args[0]](version);
                    log.Info("Completed hook, exiting...");
                    Environment.Exit(0);
                } catch (Exception ex) {
                    log.Error(ex, $"Error occurred executing user defined Squirrel hook. ({args[0]})");
                    Environment.Exit(-1);
                }
            }

            // some initial setup/state
            var myVersion = locator.CurrentlyInstalledVersion;
            var firstrun = !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLOWD_SQUIRREL_FIRSTRUN"));
            var restarted = !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLOWD_SQUIRREL_RESTART"));
            var localPackages = locator.GetLocalPackages();
            var latestLocal = locator.GetLatestLocalFullPackage();

            // if we've not just been restarted via Squirrel apply, and there is a local update available,
            // we should install it first.
            if (latestLocal != null && latestLocal.Version > myVersion) {
                log.Info($"Launching app is out-dated. Current: {myVersion}, Newest Local Available: {latestLocal.Version}");
                if (!restarted && _autoApply) {
                    log.Info("Auto apply is true, so restarting to apply update...");
                    var um = new UpdateManager(log, locator);
                    um.ApplyUpdatesAndRestart(args);
                }
            }

            // clean up old versions of the app
            var pkgPath = locator.PackagesDir;
            foreach (var package in localPackages) {
                if (package.Version == latestLocal.Version || package.Version == myVersion) {
                    continue;
                }
                try {
                    log.Info("Removing old package: " + package.OriginalFilename);
                    var p = Path.Combine(pkgPath, package.OriginalFilename);
                    File.Delete(p);
                } catch (Exception ex) {
                    log.Error(ex, $"Failed to remove old package '{package.OriginalFilename}'");
                }
            }

            // run non-exiting user hooks
            if (firstrun) {
                try {
                    _firstrun(myVersion);
                } catch (Exception ex) {
                    log.Error(ex, $"Error occurred executing user defined Squirrel hook. (firstrun)");
                }
            }
            if (restarted) {
                try {
                    _restarted(myVersion);
                } catch (Exception ex) {
                    log.Error(ex, $"Error occurred executing user defined Squirrel hook. (restarted)");
                }
            }
        }
    }
}
