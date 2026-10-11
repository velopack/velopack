#if NET8_0_OR_GREATER
using System;
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
    /// </summary>
    [SupportedOSPlatform("osx")]
    internal static unsafe class AppleEventHandler
    {
        private const string LibObjc = "/usr/lib/libobjc.dylib";
        private const string FoundationFramework = "/System/Library/Frameworks/Foundation.framework/Foundation";
        private const string HandlerClassName = "VelopackAppleEventHandler";
        private const string WillFinishLaunchingNotification = "NSApplicationWillFinishLaunchingNotification";

        private const string SelGetUrlEvent = "handleGetURLEvent:withReplyEvent:";
        private const string SelOpenDocumentsEvent = "handleOpenDocumentsEvent:withReplyEvent:";
        private const string SelWillFinishLaunching = "applicationWillFinishLaunching:";

        // four-char codes from AppleEvents.h
        private const uint kInternetEventClass = 0x4755524C; // 'GURL'
        private const uint kAEGetURL = 0x4755524C; // 'GURL'
        private const uint kCoreEventClass = 0x61657674; // 'aevt'
        private const uint kAEOpenDocuments = 0x6F646F63; // 'odoc'
        private const uint keyDirectObject = 0x2D2D2D2D; // '----'

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
        private static extern void objc_msgSend_SetEventHandler(IntPtr receiver, IntPtr sel, IntPtr handler, IntPtr selector, uint eventClass,
            uint eventId);

        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_AddObserver(IntPtr receiver, IntPtr sel, IntPtr observer, IntPtr selector, IntPtr name, IntPtr obj);

        private static readonly object s_lock = new();
        private static Action<string>? s_fileCallback;
        private static Action<string>? s_urlCallback;
        private static IVelopackLogger? s_log;
        private static IntPtr s_handler; // our NSObject subclass instance, retained for the process lifetime

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

                if (s_handler == IntPtr.Zero) {
                    // NSAppleEventManager / NSNotificationCenter live in Foundation; make sure it is loaded even in a
                    // process which has not touched any Cocoa framework yet.
                    NativeLibrary.TryLoad(FoundationFramework, out _);
                    s_handler = CreateHandlerInstance();

                    var center = objc_msgSend(objc_getClass("NSNotificationCenter"), Sel("defaultCenter"));
                    var name = NewNSString(WillFinishLaunchingNotification);
                    objc_msgSend_AddObserver(center, Sel("addObserver:selector:name:object:"), s_handler, Sel(SelWillFinishLaunching), name,
                        IntPtr.Zero);
                }

                RegisterEventHandlers();
                log.Info("Installed macOS Apple Event handlers for file/URL associations.");
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
            var willFinish = (IntPtr) (delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>) &ApplicationWillFinishLaunching;
            if (class_addMethod(cls, Sel(SelGetUrlEvent), getUrl, "v@:@@") == 0
                || class_addMethod(cls, Sel(SelOpenDocumentsEvent), openDocs, "v@:@@") == 0
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
        private static void HandleGetUrlEvent(IntPtr self, IntPtr sel, IntPtr evt, IntPtr replyEvent)
        {
            try {
                var descriptor = objc_msgSend(evt, Sel("paramDescriptorForKeyword:"), keyDirectObject);
                var url = descriptor == IntPtr.Zero ? null : GetString(objc_msgSend(descriptor, Sel("stringValue")));
                if (String.IsNullOrEmpty(url)) {
                    s_log?.Warn("Received kAEGetURL Apple Event without a URL.");
                    return;
                }

                Dispatch(s_urlCallback, url!, VelopackApp.HookCliUrlAssociation);
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

                // descriptor lists are 1-based. The direct object may also be a single descriptor rather than a list, in
                // which case numberOfItems still reports 1 and descriptorAtIndex:1 returns it.
                var count = (long) objc_msgSend(list, Sel("numberOfItems"));
                for (nint i = 1; i <= count; i++) {
                    var item = objc_msgSend(list, Sel("descriptorAtIndex:"), (IntPtr) i);
                    if (item == IntPtr.Zero) {
                        continue;
                    }

                    var path = GetPosixPath(item);
                    if (String.IsNullOrEmpty(path)) {
                        s_log?.Warn("Could not resolve a file path from a kAEOpenDocuments Apple Event item.");
                        continue;
                    }

                    Dispatch(s_fileCallback, path!, VelopackApp.HookCliFileAssociation);
                }
            } catch (Exception ex) {
                s_log?.Error(ex, "Error handling kAEOpenDocuments Apple Event.");
            }
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

        private static void Dispatch(Action<string>? callback, string value, string kind)
        {
            if (callback == null) {
                return;
            }

            try {
                callback(value);
            } catch (Exception ex) {
                s_log?.Error(ex, $"Error occurred executing user defined Velopack hook. ({kind})");
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
