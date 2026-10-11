using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Util;

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
        /// <summary> CLI flag the OS passes (via the registered association) when the app is opened to handle a file. </summary>
        internal const string HookCliFileAssoc = "--veloapp-fileassoc";
        /// <summary> CLI flag the OS passes (via the registered protocol) when the app is opened to handle a URL. </summary>
        internal const string HookCliUrlProto = "--veloapp-urlproto";

        /// <summary> How long <see cref="Run"/> waits for macOS to deliver the launch Apple Event before restarting to apply an update. </summary>
        private static readonly TimeSpan LaunchEventDrainTimeout = TimeSpan.FromSeconds(2);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        [DllImport("kernel32.dll", EntryPoint = "GetCommandLineW")]
        private static extern IntPtr GetCommandLineW();

        private VelopackHook? _install;
        private VelopackHook? _update;
        private VelopackHook? _obsolete;
        private VelopackHook? _uninstall;
        private VelopackHook? _firstrun;
        private VelopackHook? _restarted;
        private Action<string>? _fileAssoc;
        private Action<string>? _urlProto;
        private string[]? _args;
        private bool _autoApply = true;
        private string? _customAumid;
        private IVelopackLocator? _customLocator;
        private IVelopackLogger? _customLogger;

        private VelopackApp()
        {
        }

        /// <summary>
        /// Creates and returns a new Velopack application builder.
        /// </summary>
        public static VelopackApp Build() => new();

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
        /// Override the Application User Model ID (AUMID) set for this process on Windows.
        /// By default, the AUMID is read from the package manifest (shortcutAumid), falling back to "velopack.{AppId}".
        /// </summary>
        public VelopackApp SetAppUserModelId(string aumid)
        {
            _customAumid = aumid;
            return this;
        }

        /// <summary>
        /// Override the default <see cref="IVelopackLocator"/> used to search for application paths.
        /// This will be cached and potentially re-used throughout the lifetime of the application.
        /// </summary>
        public VelopackApp SetLocator(IVelopackLocator locator)
        {
            _customLocator = locator;
            return this;
        }

        /// <summary>
        /// Adds a custom logger to the Velopack application. This will be used for all Velopack diagnostic
        /// messages in addition to the default log file location. This will be cached and re-used throughout
        /// the lifetime of the application. If you have also provided a custom locator, this logger will be ignored.
        /// </summary>
        public VelopackApp SetLogger(IVelopackLogger logger)
        {
            _customLogger = logger;
            return this;
        }

        /// <summary>
        /// This hook is triggered when the application is started for the first time after installation.
        /// </summary>
        public VelopackApp OnFirstRun(VelopackHook hook)
        {
            _firstrun += hook;
            return this;
        }

        /// <summary>
        /// This hook is triggered when the application is restarted by Velopack after installing updates.
        /// </summary>
        public VelopackApp OnRestarted(VelopackHook hook)
        {
            _restarted += hook;
            return this;
        }

        /// <summary>
        /// This callback is invoked when the operating system opens the application to handle a file whose extension
        /// was registered with the <c>--fileAssociations</c> packaging option. The argument is the full path of the file.
        /// <para>
        /// On Windows, the registered association launches the application with <c>--veloapp-fileassoc "{path}"</c>.
        /// <see cref="Run"/> recovers the path and invokes this callback at the end of startup (after the first-run and
        /// restarted hooks); normal startup then continues. The callback runs on the thread calling <see cref="Run"/>.
        /// </para>
        /// <para>
        /// On macOS, files are delivered as Apple Events (<c>kAEOpenDocuments</c>) rather than command line arguments, so
        /// this callback may be invoked at any time during the application lifetime and more than once (once per file).
        /// Events are dispatched on the main thread by the Cocoa run loop, so the application must run one (any
        /// Cocoa based UI framework does). Registering this callback replaces the UI framework's own open-file handling.
        /// Delivered only by the net8.0 or later builds of the Velopack package; apps resolving the netstandard2.0 or
        /// net472 build (including net6.0/net7.0 consumers) get a logged warning and the callback is never invoked on macOS.
        /// </para>
        /// <para>
        /// The launch is never lost to an update: when <see cref="Run"/> restarts the application to apply a pending
        /// update, the file(s) are forwarded to the new version, which invokes this callback once per file. On macOS
        /// this means <see cref="Run"/> first waits for the launch Apple Event (up to about 2 seconds, only when an
        /// update is about to be applied).
        /// </para>
        /// <para>Not supported on Linux; the callback is never invoked there.</para>
        /// </summary>
        public VelopackApp OnFileAssoc(Action<string> callback)
        {
            _fileAssoc += callback;
            return this;
        }

        /// <summary>
        /// This callback is invoked when the operating system opens the application to handle a URL whose scheme was
        /// registered with the <c>--urlProtocols</c> packaging option. The argument is the complete URL.
        /// <para>
        /// On Windows, the registered protocol launches the application with <c>--veloapp-urlproto "{url}"</c>.
        /// <see cref="Run"/> recovers the URL verbatim from the raw command line (so URLs containing quotes or other
        /// shell-significant characters survive) and invokes this callback at the end of startup (after the first-run and
        /// restarted hooks); normal startup then continues. The callback runs on the thread calling <see cref="Run"/>.
        /// </para>
        /// <para>
        /// On macOS, URLs are delivered as Apple Events (<c>kAEGetURL</c>) rather than command line arguments, so this
        /// callback may be invoked at any time during the application lifetime and more than once. Events are dispatched
        /// on the main thread by the Cocoa run loop, so the application must run one (any Cocoa based UI framework does).
        /// Registering this callback replaces the UI framework's own open-URL handling.
        /// Delivered only by the net8.0 or later builds of the Velopack package; apps resolving the netstandard2.0 or
        /// net472 build (including net6.0/net7.0 consumers) get a logged warning and the callback is never invoked on macOS.
        /// </para>
        /// <para>
        /// The launch is never lost to an update: when <see cref="Run"/> restarts the application to apply a pending
        /// update, the URL is forwarded to the new version, which invokes this callback. On macOS this means
        /// <see cref="Run"/> first waits for the launch Apple Event (up to about 2 seconds, only when an update is
        /// about to be applied).
        /// </para>
        /// <para>Not supported on Linux; the callback is never invoked there.</para>
        /// </summary>
        public VelopackApp OnUrlProto(Action<string> callback)
        {
            _urlProto += callback;
            return this;
        }

        /// <summary>
        /// WARNING: FastCallback hooks are run during critical stages of Velopack operations.
        /// Your code will be run and then <see cref="Environment.Exit(int)"/> will be called.
        /// If your code has not completed within 30 seconds, it will be terminated.
        /// Only supported on windows; On other operating systems, this will never be called.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public VelopackApp OnAfterInstallFastCallback(VelopackHook hook)
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
        public VelopackApp OnAfterUpdateFastCallback(VelopackHook hook)
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
        public VelopackApp OnBeforeUpdateFastCallback(VelopackHook hook)
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
        public VelopackApp OnBeforeUninstallFastCallback(VelopackHook hook)
        {
            _uninstall += hook;
            return this;
        }

        /// <summary>
        /// Runs the Velopack application startup code and triggers any configured hooks.
        /// </summary>
        public void Run()
        {
            var args = _args ?? Environment.GetCommandLineArgs().Skip(1).ToArray();

            if (VelopackLocator.IsCurrentSet) {
                VelopackLocator.Current.Log.Error(
                    "VelopackApp.Build().Run() was called more than once. This is not allowed and can lead to unexpected behaviour.");
            }

            if (_customLocator != null) {
                VelopackLocator.SetCurrentLocator(_customLocator);
            }

            var locator = VelopackLocator.GetCurrentOrCreateDefault(null, _customLogger);
            var log = locator.Log;

            log.Info($"Starting VelopackApp.Run (library version {VelopackRuntimeInfo.VelopackNugetVersion}).");

            if (VelopackRuntimeInfo.IsWindows) {
                var appUserModelId = _customAumid ?? locator.AppUserModelId;
                if (appUserModelId != null) {
                    log.Info($"Setting current process explicit AppUserModelID to '{appUserModelId}'");
                    SetCurrentProcessExplicitAppUserModelID(appUserModelId);
                }
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
            if (args.Length >= 2 && fastExitlookup.TryGetValue(args[0], out var hook)) {
                try {
                    log.Info("Found fast exit hook: " + args[0]);
                    var version = SemanticVersion.Parse(args[1]);
                    hook(version);
                    log.Info("Completed hook, exiting...");
                    locator.Process.Exit(0);
                    return;
                } catch (Exception ex) {
                    log.Error(ex, $"Error occurred executing user defined Velopack hook. ({args[0]})");
                    locator.Process.Exit(-1);
                    return;
                }
            }

            var firstrun = !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_FIRSTRUN"));
            var restarted = !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_RESTART"));

            // an association launch ("--veloapp-urlproto {url}") is not a fast exit hook. The values are recovered
            // up-front so they can be forwarded intact if we restart to apply an update, and the callbacks run at the end.
            // On Windows a launch by the OS shell (not a restart) carries exactly one value, recovered from the raw command
            // line, also when SetArgs was used (apps commonly pass their own argv through). Only Update.exe (restart) and
            // non-Windows argv are exact and may carry several [flag, value] pairs.
            var rawCommandLine = restarted ? null : GetRawCommandLine();
            var associations = ParseAssociationArgs(args, rawCommandLine, log);
            if (associations.Count > 0 && associations.All(a => GetAssociationCallback(a.Flag) == null)) {
                // without a callback the arguments are left (and forwarded) untouched for the application to handle.
                log.Info($"No callback registered for {args[0]}, leaving the arguments to the application.");
                associations.Clear();
            } else if (associations.Count > 0) {
                // the values (paths or URLs, which may carry tokens) are kept out of the persistent log
                log.Info("Found association launch: " + String.Join(", ", associations.Select(a => $"{a.Flag} ({a.Value.Length} chars)")));
            }

            var myVersion = locator.CurrentlyInstalledVersion;
            if (myVersion != null) {
                if (!RunInstalledStartup(locator, myVersion, args, associations, firstrun, restarted)) {
                    return;
                }
            }

            if (VelopackRuntimeInfo.IsOSX && (_fileAssoc != null || _urlProto != null)) {
                InstallAppleEventHandlers(log);
            }

            foreach (var association in associations) {
                InvokeAssociationCallback(log, association.Flag, association.Value);
            }
        }

        /// <summary>
        /// The part of startup which only applies when the app is installed. Returns false if the process is exiting
        /// (restarting to apply an update), true if startup should continue.
        /// </summary>
        private bool RunInstalledStartup(IVelopackLocator locator, SemanticVersion myVersion, string[] args,
            List<(string Flag, string Value)> associations, bool firstrun, bool restarted)
        {
            var log = locator.Log;
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

                    // Hand the associations on as repeated [flag, value] pairs (leading the arguments) so the recovered
                    // values survive the restart intact; without any, the arguments are forwarded as they are.
                    var drained = DrainMacLaunchEvents(log);
                    var restartArgs = new List<string>();
                    foreach (var (flag, value) in drained.Concat(associations)) {
                        restartArgs.Add(flag);
                        restartArgs.Add(value);
                    }

                    if (associations.Count == 0) {
                        restartArgs.AddRange(args);
                    }

                    var applying = false;
                    try {
                        UpdateExe.Apply(locator, latestLocal, false, locator.Process.GetCurrentProcessId(), true, restartArgs.ToArray());
                        applying = true;
                    } catch (Exception ex) {
                        log.Error(ex, "Error applying pending updates on startup.");
                        // Startup continues in this process, so still deliver whatever the OS asked us to open. Known
                        // limitation: on macOS the drain has already finished launching NSApplication (with our oapp/rapp
                        // handlers installed) and the app simply carries on from there; it is deliberately not relaunched,
                        // which could loop forever on a persistent apply failure.
                        associations.AddRange(drained);
                    }

                    if (applying) {
                        locator.Process.Exit(0);
                        return false;
                    }
                } else {
                    log.Info(
                        "Pre-condition failed, we will not restart to apply updates. (restarted: " + restarted + ", autoApply: " + _autoApply + ")");
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

            return true;
        }

        private Action<string>? GetAssociationCallback(string flag) => flag == HookCliFileAssoc ? _fileAssoc : _urlProto;

        private void InvokeAssociationCallback(IVelopackLogger log, string flag, string value)
        {
            var callback = GetAssociationCallback(flag);
            if (callback == null) {
                return;
            }

            try {
                callback(value);
            } catch (Exception ex) {
                log.Error(ex, $"Error occurred executing user defined Velopack hook. ({flag})");
            }
        }

        /// <summary>
        /// On macOS the OS tells us what to open through a launch Apple Event, which is only delivered once a Cocoa run
        /// loop is pumped. Called right before restarting to apply an update, this pumps the run loop until that event
        /// has arrived (or <see cref="LaunchEventDrainTimeout"/> elapses) and returns the files / URLs it carried so
        /// they can be forwarded to the restarted app. Empty on other platforms, without association callbacks, and when
        /// not called on the main thread (AppKit would abort the process).
        /// <para>
        /// Known limitation: if the update is then not applied after all (the apply failed), the process carries on with
        /// NSApplication already finish-launched and our oapp/rapp handlers still installed; the caller delivers the
        /// collected values to the callbacks itself. The process is deliberately not relaunched to undo this, since that
        /// could loop forever on a persistent apply failure.
        /// </para>
        /// </summary>
        private List<(string Flag, string Value)> DrainMacLaunchEvents(IVelopackLogger log)
        {
            var drained = new List<(string Flag, string Value)>();
#if NET8_0_OR_GREATER
            if (VelopackRuntimeInfo.IsOSX && (_fileAssoc != null || _urlProto != null)) {
                try {
                    drained = Osx.AppleEventHandler.DrainLaunchEvents(_fileAssoc != null, _urlProto != null, LaunchEventDrainTimeout, log);
                } catch (Exception ex) {
                    log.Error(ex, "Failed to collect the macOS launch Apple Event before applying the update.");
                }
            }
#endif
            return drained;
        }

        [SupportedOSPlatform("osx")]
        private void InstallAppleEventHandlers(IVelopackLogger log)
        {
#if NET8_0_OR_GREATER
            try {
                Osx.AppleEventHandler.Install(_fileAssoc, _urlProto, log);
            } catch (Exception ex) {
                log.Error(ex, "Failed to install macOS Apple Event handlers for file/URL associations.");
            }
#else
            log.Warn(
                "File/URL association callbacks are not delivered on macOS by this build of Velopack " +
                "(netstandard2.0 / net472); they require the net8.0 or later build of the package.");
#endif
        }

        /// <summary>
        /// Returns the canonical (lowercase) association flag constant <paramref name="arg"/> matches, or null. Flags are
        /// matched case-insensitively, like the other <c>--veloapp-*</c> hooks.
        /// </summary>
        internal static string? CanonicalAssociationFlag(string arg)
        {
            if (String.Equals(arg, HookCliFileAssoc, StringComparison.OrdinalIgnoreCase)) {
                return HookCliFileAssoc;
            }

            if (String.Equals(arg, HookCliUrlProto, StringComparison.OrdinalIgnoreCase)) {
                return HookCliUrlProto;
            }

            return null;
        }

        /// <summary>
        /// Returns the association activations carried by <paramref name="args"/>, or an empty list when args[0] is not an
        /// association flag. When <paramref name="rawCommandLine"/> is provided (a Windows launch which is not a Velopack
        /// restart) the OS shell passed exactly one value: it is recovered from the raw command line when the flag is found
        /// there, see <see cref="RecoverValueFromRawCommandLine"/>, otherwise (synthetic <see cref="SetArgs"/>) at most
        /// one <c>[flag, value]</c> pair is read from <paramref name="args"/>. Without a raw command line (a Velopack
        /// restart, or not Windows) argv is exact and read as repeated pairs, see <see cref="AssociationPairsFromArgs"/>.
        /// </summary>
        /// <param name="args">The application arguments (without the executable path).</param>
        /// <param name="rawCommandLine">The unparsed process command line, or null to read the pairs from <paramref name="args"/>.</param>
        /// <param name="log">Receives a warning for a flag without a value.</param>
        internal static List<(string Flag, string Value)> ParseAssociationArgs(string[] args, string? rawCommandLine, IVelopackLogger? log = null)
        {
            var flag = args.Length > 0 ? CanonicalAssociationFlag(args[0]) : null;
            if (flag == null) {
                return new List<(string Flag, string Value)>();
            }

            if (rawCommandLine != null) {
                var value = RecoverValueFromRawCommandLine(flag, rawCommandLine);
                if (value != null) {
                    if (value.Length == 0) {
                        log?.Warn($"{flag} was passed without a value, ignoring.");
                        return new List<(string Flag, string Value)>();
                    }

                    return new List<(string Flag, string Value)> { (flag, value) };
                }

                // the flag is not on the real command line (SetArgs): the OS never passes more than one pair
                var single = AssociationPairsFromArgs(args, log);
                if (single.Count > 1) {
                    single.RemoveRange(1, single.Count - 1);
                }

                return single;
            }

            return AssociationPairsFromArgs(args, log);
        }

        /// <summary>
        /// Reads the leading <c>[flag, value]</c> pairs of <paramref name="args"/>, the form in which a Velopack restart
        /// forwards association activations. Parsing stops at the first element which is not an association flag; a flag
        /// with nothing, or an empty string, after it is dropped with a warning.
        /// </summary>
        internal static List<(string Flag, string Value)> AssociationPairsFromArgs(string[] args, IVelopackLogger? log = null)
        {
            var pairs = new List<(string Flag, string Value)>();
            for (var i = 0; i < args.Length; i += 2) {
                var flag = CanonicalAssociationFlag(args[i]);
                if (flag == null) {
                    break;
                }

                if (i + 1 < args.Length && args[i + 1].Length > 0) {
                    pairs.Add((flag, args[i + 1]));
                } else {
                    log?.Warn($"{flag} was passed without a value, ignoring.");
                }
            }

            return pairs;
        }

        /// <summary>
        /// Recovers the value passed after <paramref name="flag"/> in the raw command line: everything after the flag token,
        /// trimmed and with one pair of enclosing double quotes removed. This yields the exact text the shell substituted
        /// for <c>%1</c>, even for URLs containing quotes which argv parsing would mangle. Returns null when the flag is
        /// not present as a token, and an empty string when nothing follows it.
        /// </summary>
        internal static string? RecoverValueFromRawCommandLine(string flag, string rawCommandLine)
        {
            var arguments = SkipProgramPath(rawCommandLine);
            var idx = IndexOfToken(arguments, flag);
            if (idx < 0) {
                return null;
            }

            var rest = arguments.Substring(idx + flag.Length).Trim();
            if (rest.Length >= 2 && rest[0] == '"' && rest[rest.Length - 1] == '"') {
                rest = rest.Substring(1, rest.Length - 2);
            }

            return rest;
        }

        /// <summary>
        /// Returns the raw command line without its first token (the program path), which is never the flag: a quoted
        /// path runs to the closing quote, otherwise to the first whitespace.
        /// </summary>
        private static string SkipProgramPath(string rawCommandLine)
        {
            var text = rawCommandLine.TrimStart();
            int argsStart;
            if (text.Length > 0 && text[0] == '"') {
                var closingQuote = text.IndexOf('"', 1);
                argsStart = closingQuote >= 0 ? closingQuote + 1 : text.Length;
            } else {
                argsStart = 0;
                while (argsStart < text.Length && !Char.IsWhiteSpace(text[argsStart])) {
                    argsStart++;
                }
            }

            return text.Substring(argsStart);
        }

        /// <summary>
        /// Finds <paramref name="token"/> in <paramref name="text"/> as a whole whitespace-delimited word (case-insensitive).
        /// </summary>
        private static int IndexOfToken(string text, string token)
        {
            var start = 0;
            while (start < text.Length) {
                var idx = text.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) {
                    return -1;
                }

                var end = idx + token.Length;
                var boundaryBefore = idx == 0 || Char.IsWhiteSpace(text[idx - 1]);
                var boundaryAfter = end == text.Length || Char.IsWhiteSpace(text[end]);
                if (boundaryBefore && boundaryAfter) {
                    return idx;
                }

                start = end;
            }

            return -1;
        }

        /// <summary>
        /// Returns the unparsed command line of the current process on Windows (where the runtime re-parses argv and
        /// loses quoting information), or null elsewhere / on failure.
        /// </summary>
        private static string? GetRawCommandLine()
        {
            if (!VelopackRuntimeInfo.IsWindows) {
                return null;
            }

            try {
                var ptr = GetCommandLineW();
                if (ptr != IntPtr.Zero) {
                    return Marshal.PtrToStringUni(ptr);
                }
            } catch {
                // fall through to the runtime's copy
            }

            try {
                return Environment.CommandLine;
            } catch {
                return null;
            }
        }
    }
}
