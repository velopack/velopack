using System;
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
        internal const string HookCliFileAssociation = "--veloapp-file-association";
        /// <summary> CLI flag the OS passes (via the registered association) when the app is opened to handle a URL. </summary>
        internal const string HookCliUrlAssociation = "--veloapp-url-association";

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
        private Action<string>? _fileAssociation;
        private Action<string>? _urlAssociation;
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
        /// On Windows, the registered association launches the application with
        /// <c>--veloapp-file-association "{path}"</c>. <see cref="Run"/> recovers the path, forwards it if a pending
        /// update is applied first, and invokes this callback once at the end of startup (after the first-run and
        /// restarted hooks); normal startup then continues. The callback runs on the thread calling <see cref="Run"/>.
        /// </para>
        /// <para>
        /// On macOS, files are delivered as Apple Events (<c>kAEOpenDocuments</c>) rather than command line arguments, so
        /// this callback may be invoked at any time during the application lifetime and more than once (once per file).
        /// Events are dispatched on the main thread by the Cocoa run loop, so the application must run one (any
        /// Cocoa based UI framework does). Registering this callback replaces the UI framework's own open-file handling.
        /// When a pending update is auto-applied at startup on macOS the process exits before that launch's Apple Event
        /// is delivered, so the file is lost; macOS apps using associations should call
        /// <see cref="SetAutoApplyOnStartup"/>(false) and apply updates at a moment of their choosing.
        /// Delivered only by the net8.0 or later builds of the Velopack package; apps resolving the netstandard2.0 or
        /// net472 build (including net6.0/net7.0 consumers) get a logged warning and the callback is never invoked on macOS.
        /// </para>
        /// <para>Not supported on Linux; the callback is never invoked there.</para>
        /// </summary>
        public VelopackApp OnFileAssociation(Action<string> callback)
        {
            _fileAssociation += callback;
            return this;
        }

        /// <summary>
        /// This callback is invoked when the operating system opens the application to handle a URL whose scheme was
        /// registered with the <c>--urlProtocols</c> packaging option. The argument is the complete URL.
        /// <para>
        /// On Windows, the registered protocol launches the application with
        /// <c>--veloapp-url-association "{url}"</c>. <see cref="Run"/> recovers the URL verbatim from the raw command line
        /// (so URLs containing quotes or other shell-significant characters survive), forwards it if a pending update is
        /// applied first, and invokes this callback once at the end of startup (after the first-run and restarted hooks);
        /// normal startup then continues. The callback runs on the thread calling <see cref="Run"/>.
        /// </para>
        /// <para>
        /// On macOS, URLs are delivered as Apple Events (<c>kAEGetURL</c>) rather than command line arguments, so this
        /// callback may be invoked at any time during the application lifetime and more than once. Events are dispatched
        /// on the main thread by the Cocoa run loop, so the application must run one (any Cocoa based UI framework does).
        /// Registering this callback replaces the UI framework's own open-URL handling.
        /// When a pending update is auto-applied at startup on macOS the process exits before that launch's Apple Event
        /// is delivered, so the URL is lost; macOS apps using associations should call
        /// <see cref="SetAutoApplyOnStartup"/>(false) and apply updates at a moment of their choosing.
        /// Delivered only by the net8.0 or later builds of the Velopack package; apps resolving the netstandard2.0 or
        /// net472 build (including net6.0/net7.0 consumers) get a logged warning and the callback is never invoked on macOS.
        /// </para>
        /// <para>Not supported on Linux; the callback is never invoked there.</para>
        /// </summary>
        public VelopackApp OnUrlAssociation(Action<string> callback)
        {
            _urlAssociation += callback;
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

            // an association launch ("--veloapp-url-association {url}") is not a fast exit hook. The value is recovered
            // up-front so it can be forwarded intact if we restart to apply an update, and the callback runs at the end.
            // The raw command line is only trustworthy when the OS shell launched us: Update.exe (restart) and SetArgs
            // provide already-escaped/parsed args, so for those argv is the exact value.
            var rawCommandLine = _args == null && !restarted ? GetRawCommandLine() : null;
            var isAssociation = TryParseAssociationArgs(args, rawCommandLine, out var associationFlag, out var associationValue);
            if (isAssociation && GetAssociationCallback(associationFlag) == null) {
                // without a callback the arguments are left (and forwarded) untouched for the application to handle.
                log.Info($"No callback registered for {associationFlag}, leaving the arguments to the application.");
                isAssociation = false;
            } else if (isAssociation) {
                // the value (a path or URL, which may carry tokens) is kept out of the persistent log
                log.Info($"Found association launch: {associationFlag} ({associationValue.Length} chars)");
            }

            var myVersion = locator.CurrentlyInstalledVersion;
            if (myVersion != null) {
                var restartArgs = isAssociation ? new[] { associationFlag, associationValue } : args;
                if (!RunInstalledStartup(locator, myVersion, restartArgs, firstrun, restarted)) {
                    return;
                }
            }

            if (VelopackRuntimeInfo.IsOSX && (_fileAssociation != null || _urlAssociation != null)) {
                InstallAppleEventHandlers(log);
            }

            if (isAssociation) {
                InvokeAssociationCallback(log, associationFlag, associationValue);
            }
        }

        /// <summary>
        /// The part of startup which only applies when the app is installed. Returns false if the process is exiting
        /// (restarting to apply an update), true if startup should continue.
        /// </summary>
        private bool RunInstalledStartup(IVelopackLocator locator, SemanticVersion myVersion, string[] restartArgs, bool firstrun, bool restarted)
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
                    UpdateExe.Apply(locator, latestLocal, false, locator.Process.GetCurrentProcessId(), true, restartArgs);
                    locator.Process.Exit(0);
                    return false;
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

        private Action<string>? GetAssociationCallback(string flag) => flag == HookCliFileAssociation ? _fileAssociation : _urlAssociation;

        private void InvokeAssociationCallback(IVelopackLogger log, string flag, string value)
        {
            var callback = GetAssociationCallback(flag);
            if (callback == null) {
                return;
            }

            if (value.Length == 0) {
                log.Warn($"Ignoring {flag} launch with an empty value.");
                return;
            }

            try {
                callback(value);
            } catch (Exception ex) {
                log.Error(ex, $"Error occurred executing user defined Velopack hook. ({flag})");
            }
        }

        [SupportedOSPlatform("osx")]
        private void InstallAppleEventHandlers(IVelopackLogger log)
        {
#if NET8_0_OR_GREATER
            try {
                Osx.AppleEventHandler.Install(_fileAssociation, _urlAssociation, log);
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
        /// Checks whether <paramref name="args"/> is an association launch (args[0] is one of the association flags), and
        /// if so recovers the file path / URL value. See <see cref="RecoverAssociationValue"/>.
        /// </summary>
        /// <param name="args">The application arguments (without the executable path).</param>
        /// <param name="rawCommandLine">The unparsed process command line, or null to recover the value from <paramref name="args"/>.</param>
        /// <param name="flag">The canonical flag constant that matched.</param>
        /// <param name="value">The recovered value (may be empty).</param>
        internal static bool TryParseAssociationArgs(string[] args, string? rawCommandLine, out string flag, out string value)
        {
            flag = "";
            value = "";
            if (args.Length == 0) {
                return false;
            }

            if (String.Equals(args[0], HookCliFileAssociation, StringComparison.OrdinalIgnoreCase)) {
                flag = HookCliFileAssociation;
            } else if (String.Equals(args[0], HookCliUrlAssociation, StringComparison.OrdinalIgnoreCase)) {
                flag = HookCliUrlAssociation;
            } else {
                return false;
            }

            value = RecoverAssociationValue(flag, args, rawCommandLine);
            return true;
        }

        /// <summary>
        /// Recovers the value passed after an association flag. When <paramref name="rawCommandLine"/> is provided, the value
        /// is everything after the flag token, trimmed and with one pair of enclosing double quotes removed. This yields the
        /// exact text the shell substituted for <c>%1</c>, even for URLs containing quotes which argv parsing would mangle.
        /// Otherwise (no raw command line available, or args were supplied explicitly) args[1..] are joined with a space.
        /// </summary>
        internal static string RecoverAssociationValue(string flag, string[] args, string? rawCommandLine)
        {
            if (rawCommandLine != null) {
                var arguments = SkipProgramPath(rawCommandLine);
                var idx = IndexOfToken(arguments, flag);
                if (idx >= 0) {
                    var rest = arguments.Substring(idx + flag.Length).Trim();
                    if (rest.Length >= 2 && rest[0] == '"' && rest[rest.Length - 1] == '"') {
                        rest = rest.Substring(1, rest.Length - 2);
                    }

                    return rest;
                }
            }

            return String.Join(" ", args.Skip(1));
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
                return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUni(ptr);
            } catch {
                return null;
            }
        }
    }
}
