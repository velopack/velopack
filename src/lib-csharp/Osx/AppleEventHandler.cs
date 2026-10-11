#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Velopack.Logging;

namespace Velopack.Osx
{
    /// <summary>
    /// Receives kAEGetURL (custom URL schemes) and kAEOpenDocuments (file associations) Apple Events and forwards them to
    /// the <see cref="VelopackApp"/> association callbacks. LaunchServices delivers these as events to the running process
    /// (never as argv), so the app needs a Cocoa run loop on the main thread for them to be dispatched.
    /// <para>
    /// This talks to the Objective-C runtime directly (a runtime-created NSObject subclass whose methods are
    /// <see cref="UnmanagedCallersOnlyAttribute"/> function pointers), so it has no AppKit or binding dependency and is
    /// NativeAOT / trimming safe. AppKit's <c>-[NSApplication finishLaunching]</c> installs its own handlers for the same
    /// events, so besides installing immediately we also observe <c>NSApplicationWillFinishLaunchingNotification</c> and
    /// re-install ours then, which is the documented point for an app to claim these events.
    /// </para>
    /// <para>
    /// When <see cref="VelopackApp.Run"/> is about to restart the process to apply a pending update, the launch event has
    /// not been delivered yet (no run loop has run). <see cref="DrainLaunchEvents"/> then takes over NSApplication itself
    /// (loaded at runtime): it finishes launching, pumps events until the launch event (oapp / odoc / GURL) has arrived
    /// and returns the collected values instead of invoking the callbacks, so they can be forwarded to the restarted app.
    /// </para>
    /// </summary>
    [SupportedOSPlatform("osx")]
    internal static unsafe class AppleEventHandler
    {
        private const string LibObjc = "/usr/lib/libobjc.dylib";
        private const string FoundationFramework = "/System/Library/Frameworks/Foundation.framework/Foundation";
        private const string AppKitFramework = "/System/Library/Frameworks/AppKit.framework/AppKit";
        private const string HandlerClassName = "VelopackAppleEventHandler";
        private const string WillFinishLaunchingNotification = "NSApplicationWillFinishLaunchingNotification";
        private const string DefaultRunLoopMode = "kCFRunLoopDefaultMode"; // NSDefaultRunLoopMode, compared by value

        private const string SelGetUrlEvent = "handleGetURLEvent:withReplyEvent:";
        private const string SelOpenDocumentsEvent = "handleOpenDocumentsEvent:withReplyEvent:";
        private const string SelOpenApplicationEvent = "handleOpenApplicationEvent:withReplyEvent:";
        private const string SelWillFinishLaunching = "applicationWillFinishLaunching:";

        // four-char codes from AppleEvents.h
        private const uint kInternetEventClass = 0x4755524C; // 'GURL'
        private const uint kAEGetURL = 0x4755524C; // 'GURL'
        private const uint kCoreEventClass = 0x61657674; // 'aevt'
        private const uint kAEOpenDocuments = 0x6F646F63; // 'odoc'
        private const uint kAEOpenApplication = 0x6F617070; // 'oapp'
        private const uint kAEReopenApplication = 0x72617070; // 'rapp'
        private const uint keyDirectObject = 0x2D2D2D2D; // '----'

        /// <summary> How long each nextEventMatchingMask: call blocks while draining; the loop checks for the launch event in between. </summary>
        private const double DrainPumpIntervalSeconds = 0.05;

        [DllImport(LibObjc)]
        private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(LibObjc)]
        private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(LibObjc)]
        private static extern IntPtr objc_allocateClassPair(IntPtr superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint extraBytes);

        [DllImport(LibObjc)]
        private static extern void objc_registerClassPair(IntPtr cls);

        // BOOL is a signed char on every macOS target; returned as a byte to keep the signature blittable.
        [DllImport(LibObjc)]
        private static extern byte class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel);

        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel, IntPtr arg1);

        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel, uint arg1);

        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel, double arg1);

        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_SetEventHandler(IntPtr receiver, IntPtr sel, IntPtr handler, IntPtr selector, uint eventClass,
            uint eventId);

        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_AddObserver(IntPtr receiver, IntPtr sel, IntPtr observer, IntPtr selector, IntPtr name, IntPtr obj);

        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_NextEvent(IntPtr receiver, IntPtr sel, nuint mask, IntPtr untilDate, IntPtr mode, byte dequeue);

        [DllImport(LibObjc)]
        private static extern IntPtr objc_autoreleasePoolPush();

        [DllImport(LibObjc)]
        private static extern void objc_autoreleasePoolPop(IntPtr pool);

        /// <summary> Non-zero when called on the main thread, the only thread AppKit allows NSApplication to be driven from. </summary>
        [DllImport("libSystem.dylib")]
        private static extern int pthread_main_np();

        private static readonly object s_lock = new();
        private static Action<string>? s_fileCallback;
        private static Action<string>? s_urlCallback;
        private static IVelopackLogger? s_log;
        private static IntPtr s_handler; // our NSObject subclass instance, retained for the process lifetime
        private static List<(string Flag, string Value)>? s_collecting; // non-null while DrainLaunchEvents runs
        private static volatile bool s_launchSeen;

        /// <summary>
        /// Installs (or re-targets) the Apple Event handlers for whichever callbacks are non-null.
        /// Safe to call more than once; later calls replace the callbacks.
        /// </summary>
        public static void Install(Action<string>? fileCallback, Action<string>? urlCallback, IVelopackLogger log)
        {
            lock (s_lock) {
                s_fileCallback = fileCallback;
                s_urlCallback = urlCallback;
                s_log = log;
                EnsureHandlerInstance();
                RegisterEventHandlers();
                log.Info("Installed macOS Apple Event handlers for file/URL associations.");
            }
        }

        /// <summary>
        /// Pumps the Cocoa event loop until the launch Apple Event has been delivered (or <paramref name="timeout"/>
        /// elapses) and returns the files / URLs it asked us to open as (flag, value) pairs, without invoking any callback.
        /// Only for use right before the process exits to apply an update: it finishes launching NSApplication on behalf
        /// of the app. Must be called on the main thread (AppKit aborts the process with NSInternalInconsistencyException
        /// otherwise); on any other thread it logs a warning and returns an empty list.
        /// <para>
        /// Known limitation: if the update is then not applied after all (the apply failed), the process carries on with
        /// NSApplication already finish-launched and our oapp/rapp handlers still installed; the caller delivers the
        /// collected values to the callbacks itself. The process is deliberately not relaunched to undo this, since that
        /// could loop forever on a persistent apply failure.
        /// </para>
        /// </summary>
        public static List<(string Flag, string Value)> DrainLaunchEvents(bool files, bool urls, TimeSpan timeout, IVelopackLogger log)
        {
            if (pthread_main_np() == 0) {
                log.Warn("Not on the main thread, a launch Apple Event cannot be collected before updating.");
                return new List<(string Flag, string Value)>();
            }

            var stopwatch = Stopwatch.StartNew();
            lock (s_lock) {
                s_log = log;
                // placeholders keep RegisterEventHandlers claiming the events while collecting; a later Install replaces them
                s_fileCallback ??= files ? _ => { } : null;
                s_urlCallback ??= urls ? _ => { } : null;
                s_collecting = new List<(string Flag, string Value)>();
                s_launchSeen = false;
            }

            List<(string Flag, string Value)> collected;
            try {
                PumpUntilLaunchEvent(timeout, stopwatch, log);
            } finally {
                // always leaves collect mode, so an exception can never leave later events buffered instead of delivered
                lock (s_lock) {
                    collected = s_collecting ?? new List<(string Flag, string Value)>();
                    s_collecting = null;
                }

                if (s_launchSeen) {
                    log.Info($"Launch Apple Event received after {stopwatch.Elapsed}, {collected.Count} value(s) collected.");
                } else {
                    log.Warn($"No launch Apple Event was received within {timeout}.");
                }
            }

            return collected;
        }

        /// <summary> The body of <see cref="DrainLaunchEvents"/>: returns once the launch event was seen, on timeout or when giving up. </summary>
        private static void PumpUntilLaunchEvent(TimeSpan timeout, Stopwatch stopwatch, IVelopackLogger log)
        {
            EnsureHandlerInstance();
            if (!NativeLibrary.TryLoad(AppKitFramework, out _)) {
                log.Warn("Unable to load AppKit, a launch Apple Event cannot be collected before updating.");
                return;
            }

            var pool = objc_autoreleasePoolPush();
            try {
                var app = objc_msgSend(objc_getClass("NSApplication"), Sel("sharedApplication"));
                if (app == IntPtr.Zero) {
                    log.Warn("NSApplication is unavailable, a launch Apple Event cannot be collected before updating.");
                    return;
                }

                // Collect mode also claims the plain open-application event, so a launch without anything to open ends
                // the wait too. Install before and after finishLaunching: AppKit replaces the handlers inside it (and
                // our WillFinishLaunching observer reinstalls them), but a framework may have already finished launching.
                lock (s_lock) RegisterEventHandlers();
                objc_msgSend(app, Sel("finishLaunching"));
                lock (s_lock) RegisterEventHandlers();

                var mode = NewNSString(DefaultRunLoopMode);
                var nextEvent = Sel("nextEventMatchingMask:untilDate:inMode:dequeue:");
                var sendEvent = Sel("sendEvent:");
                var dateWithInterval = Sel("dateWithTimeIntervalSinceNow:");
                var nsDate = objc_getClass("NSDate");
                while (!s_launchSeen && stopwatch.Elapsed < timeout) {
                    var inner = objc_autoreleasePoolPush();
                    try {
                        // Apple Events are dispatched (AEProcessAppleEvent) from inside nextEventMatchingMask:, which
                        // is also how NSApplication's own run loop receives them.
                        var until = objc_msgSend(nsDate, dateWithInterval, DrainPumpIntervalSeconds);
                        var evt = objc_msgSend_NextEvent(app, nextEvent, nuint.MaxValue /* NSEventMaskAny */, until, mode, 1);
                        if (evt != IntPtr.Zero) {
                            objc_msgSend(app, sendEvent, evt);
                        }
                    } finally {
                        objc_autoreleasePoolPop(inner);
                    }
                }
            } finally {
                objc_autoreleasePoolPop(pool);
            }
        }

        private static void EnsureHandlerInstance()
        {
            lock (s_lock) {
                if (s_handler != IntPtr.Zero) {
                    return;
                }

                // NSAppleEventManager / NSNotificationCenter live in Foundation; make sure it is loaded even in a
                // process which has not touched any Cocoa framework yet.
                NativeLibrary.TryLoad(FoundationFramework, out _);
                s_handler = CreateHandlerInstance();

                var center = objc_msgSend(objc_getClass("NSNotificationCenter"), Sel("defaultCenter"));
                var name = NewNSString(WillFinishLaunchingNotification);
                objc_msgSend_AddObserver(center, Sel("addObserver:selector:name:object:"), s_handler, Sel(SelWillFinishLaunching), name,
                    IntPtr.Zero);
            }
        }

        private static IntPtr CreateHandlerInstance()
        {
            // Another copy of Velopack in this process may already have registered the class, with methods bound to
            // its own statics. Pick an unused name so our function pointers are the ones that run.
            var className = HandlerClassName;
            for (var i = 2; objc_getClass(className) != IntPtr.Zero; i++) {
                className = HandlerClassName + i;
            }

            var cls = objc_allocateClassPair(objc_getClass("NSObject"), className, 0);
            if (cls == IntPtr.Zero) {
                throw new InvalidOperationException($"objc_allocateClassPair failed for '{className}'.");
            }

            var getUrl = (IntPtr) (delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>) &HandleGetUrlEvent;
            var openDocs = (IntPtr) (delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>) &HandleOpenDocumentsEvent;
            var openApp = (IntPtr) (delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>) &HandleOpenApplicationEvent;
            var willFinish = (IntPtr) (delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>) &ApplicationWillFinishLaunching;
            if (class_addMethod(cls, Sel(SelGetUrlEvent), getUrl, "v@:@@") == 0
                || class_addMethod(cls, Sel(SelOpenDocumentsEvent), openDocs, "v@:@@") == 0
                || class_addMethod(cls, Sel(SelOpenApplicationEvent), openApp, "v@:@@") == 0
                || class_addMethod(cls, Sel(SelWillFinishLaunching), willFinish, "v@:@") == 0) {
                throw new InvalidOperationException("class_addMethod failed.");
            }

            objc_registerClassPair(cls);

            var instance = objc_msgSend(objc_msgSend(cls, Sel("alloc")), Sel("init"));
            if (instance == IntPtr.Zero) {
                throw new InvalidOperationException($"Failed to instantiate '{className}'.");
            }

            return instance;
        }

        private static void RegisterEventHandlers()
        {
            var manager = objc_msgSend(objc_getClass("NSAppleEventManager"), Sel("sharedAppleEventManager"));
            if (manager == IntPtr.Zero) {
                throw new InvalidOperationException("NSAppleEventManager.sharedAppleEventManager returned nil.");
            }

            var setHandler = Sel("setEventHandler:andSelector:forEventClass:andEventID:");
            if (s_urlCallback != null) {
                objc_msgSend_SetEventHandler(manager, setHandler, s_handler, Sel(SelGetUrlEvent), kInternetEventClass, kAEGetURL);
            }

            if (s_fileCallback != null) {
                objc_msgSend_SetEventHandler(manager, setHandler, s_handler, Sel(SelOpenDocumentsEvent), kCoreEventClass, kAEOpenDocuments);
            }

            if (s_collecting != null) {
                // only while draining: AppKit's own open/reopen handling is left alone for the app's lifetime
                objc_msgSend_SetEventHandler(manager, setHandler, s_handler, Sel(SelOpenApplicationEvent), kCoreEventClass, kAEOpenApplication);
                objc_msgSend_SetEventHandler(manager, setHandler, s_handler, Sel(SelOpenApplicationEvent), kCoreEventClass, kAEReopenApplication);
            }
        }

        [UnmanagedCallersOnly]
        private static void ApplicationWillFinishLaunching(IntPtr self, IntPtr sel, IntPtr notification)
        {
            try {
                lock (s_lock) {
                    RegisterEventHandlers();
                }
            } catch (Exception ex) {
                s_log?.Error(ex, "Failed to re-install Apple Event handlers on NSApplicationWillFinishLaunchingNotification.");
            }
        }

        [UnmanagedCallersOnly]
        private static void HandleOpenApplicationEvent(IntPtr self, IntPtr sel, IntPtr evt, IntPtr replyEvent)
        {
            s_launchSeen = true;
        }

        [UnmanagedCallersOnly]
        private static void HandleGetUrlEvent(IntPtr self, IntPtr sel, IntPtr evt, IntPtr replyEvent)
        {
            try {
                var descriptor = objc_msgSend(evt, Sel("paramDescriptorForKeyword:"), keyDirectObject);
                var url = descriptor == IntPtr.Zero ? null : GetString(objc_msgSend(descriptor, Sel("stringValue")));
                if (String.IsNullOrEmpty(url)) {
                    s_log?.Warn("Received kAEGetURL Apple Event without a URL.");
                    return;
                }

                Dispatch(s_urlCallback, url!, VelopackApp.HookCliUrlProto);
            } catch (Exception ex) {
                s_log?.Error(ex, "Error handling kAEGetURL Apple Event.");
            }
        }

        [UnmanagedCallersOnly]
        private static void HandleOpenDocumentsEvent(IntPtr self, IntPtr sel, IntPtr evt, IntPtr replyEvent)
        {
            try {
                var list = objc_msgSend(evt, Sel("paramDescriptorForKeyword:"), keyDirectObject);
                if (list == IntPtr.Zero) {
                    s_log?.Warn("Received kAEOpenDocuments Apple Event without a document list.");
                    return;
                }

                // The direct object is normally a list of file descriptors (1-based), but tolerate a single bare
                // descriptor (numberOfItems reports no items): it is then resolved as the one file, like the Rust library.
                var count = (long) objc_msgSend(list, Sel("numberOfItems"));
                var delivered = 0;
                if (count <= 0) {
                    delivered += DispatchDocument(list);
                }

                for (nint i = 1; i <= count; i++) {
                    delivered += DispatchDocument(objc_msgSend(list, Sel("descriptorAtIndex:"), (IntPtr) i));
                }

                if (delivered == 0) {
                    s_log?.Warn("Received kAEOpenDocuments Apple Event without any file.");
                }
            } catch (Exception ex) {
                s_log?.Error(ex, "Error handling kAEOpenDocuments Apple Event.");
            }
        }

        /// <summary> Resolves one kAEOpenDocuments descriptor to a path and dispatches it; returns 1 when it was dispatched. </summary>
        private static int DispatchDocument(IntPtr descriptor)
        {
            if (descriptor == IntPtr.Zero) {
                return 0;
            }

            var path = GetPosixPath(descriptor);
            if (String.IsNullOrEmpty(path)) {
                s_log?.Warn("Could not resolve a file path from a kAEOpenDocuments Apple Event item.");
                return 0;
            }

            Dispatch(s_fileCallback, path!, VelopackApp.HookCliFileAssoc);
            return 1;
        }

        /// <summary>
        /// Converts an item of the kAEOpenDocuments direct object (an alias / bookmark / file URL descriptor) to a POSIX path.
        /// </summary>
        private static string? GetPosixPath(IntPtr descriptor)
        {
            // -[NSAppleEventDescriptor fileURLValue] (macOS 10.11+) coerces to typeFileURL and builds the NSURL for us.
            var nsUrl = objc_msgSend(descriptor, Sel("fileURLValue"));
            if (nsUrl != IntPtr.Zero) {
                var posix = GetString(objc_msgSend(nsUrl, Sel("path")));
                if (!String.IsNullOrEmpty(posix)) {
                    return posix;
                }
            }

            var text = GetString(objc_msgSend(descriptor, Sel("stringValue")));
            if (String.IsNullOrEmpty(text)) {
                return null;
            }

            return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : text;
        }

        /// <summary> Passes a received value to its callback, or buffers it while <see cref="DrainLaunchEvents"/> is collecting. </summary>
        private static void Dispatch(Action<string>? callback, string value, string flag)
        {
            // the value (a path or URL, which may carry tokens) is kept out of the persistent log
            s_log?.Info($"Association activated ({flag}), {value.Length} chars.");
            lock (s_lock) {
                if (s_collecting != null) {
                    s_collecting.Add((flag, value));
                    s_launchSeen = true;
                    return;
                }
            }

            if (callback == null) {
                return;
            }

            try {
                callback(value);
            } catch (Exception ex) {
                s_log?.Error(ex, $"Error occurred executing user defined Velopack hook. ({flag})");
            }
        }

        private static IntPtr Sel(string name) => sel_registerName(name);

        /// <summary> Returns a +1 retained NSString (never released; only used for process-lifetime registrations). </summary>
        private static IntPtr NewNSString(string value)
        {
            var utf8 = Marshal.StringToCoTaskMemUTF8(value);
            try {
                return objc_msgSend(objc_msgSend(objc_getClass("NSString"), Sel("alloc")), Sel("initWithUTF8String:"), utf8);
            } finally {
                Marshal.FreeCoTaskMem(utf8);
            }
        }

        private static string? GetString(IntPtr nsString)
        {
            if (nsString == IntPtr.Zero) {
                return null;
            }

            return Marshal.PtrToStringUTF8(objc_msgSend(nsString, Sel("UTF8String")));
        }
    }
}
#endif
