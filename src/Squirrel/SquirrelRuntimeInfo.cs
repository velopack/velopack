using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NuGet.Versioning;
using Squirrel.SimpleSplat;

#if !NETFRAMEWORK
using InteropArchitecture = System.Runtime.InteropServices.Architecture;
#endif

#if !NET6_0_OR_GREATER
namespace System.Runtime.Versioning
{
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    internal class SupportedOSPlatformGuardAttribute : Attribute
    {
        public SupportedOSPlatformGuardAttribute(string platformName) { }
    }
}
#endif

#if NETFRAMEWORK || NETSTANDARD2_0_OR_GREATER
namespace System.Runtime.Versioning
{
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    internal class SupportedOSPlatformAttribute : Attribute
    {
        public SupportedOSPlatformAttribute(string platformName) { }
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif

namespace Squirrel
{
    // constants from winnt.h
    /// <summary> The Runtime CPU Architecture </summary>
    public enum RuntimeCpu : ushort
    {
        /// <summary> Unknown or unsupported </summary>
        Unknown = 0,

        /// <summary> Intel x86 </summary>
        x86 = 0x014c,

        /// <summary> x64 / Amd64 </summary>
        x64 = 0x8664,

        /// <summary> Arm64 </summary>
        arm64 = 0xAA64,
    }

    /// <summary>
    /// Convenience class which provides runtime information about the current executing process, 
    /// in a way that is safe in older and newer versions of the framework.
    /// </summary>
    public static class SquirrelRuntimeInfo
    {
        /// <summary> The current compiled Squirrel display version. </summary>
        public static string SquirrelDisplayVersion { get; }

        /// <summary> The current compiled Squirrel NuGetVersion. </summary>
        public static NuGetVersion SquirrelNugetVersion { get; }

        /// <summary> The current compiled Squirrel assembly file version. </summary>
        public static string SquirrelFileVersion => ThisAssembly.AssemblyFileVersion;

        /// <summary> The path on disk of the entry assembly. </summary>
        public static string EntryExePath { get; }

        /// <summary> Gets the directory that the assembly resolver uses to probe for assemblies. </summary>
        public static string BaseDirectory { get; }

        /// <summary> Check if the current application is a published SingleFileBundle. </summary>
        public static bool IsSingleFile { get; }

        /// <summary> The current machine architecture, ignoring the current process / pe architecture. </summary>
        public static RuntimeCpu SystemArchitecture { get; private set; }

        /// <summary> The name of the current OS - eg. 'windows', 'linux', or 'osx'. </summary>
        public static string SystemOsName { get; private set; }

        /// <summary> True if executing on a Windows platform. </summary>
        [SupportedOSPlatformGuard("windows")]
        public static bool IsWindows => SystemOsName == "windows";

        /// <summary> True if executing on a Linux platform. </summary>
        [SupportedOSPlatformGuard("linux")]
        public static bool IsLinux => SystemOsName == "linux";

        /// <summary> True if executing on a MacOS / OSX platform. </summary>
        [SupportedOSPlatformGuard("osx")]
        public static bool IsOSX => SystemOsName == "osx";

        /// <summary> The <see cref="StringComparer"/> that should be used when comparing local file-system paths. </summary>
        public static StringComparer PathStringComparer =>
            IsWindows ? StringComparer.InvariantCultureIgnoreCase : StringComparer.InvariantCulture;

        /// <summary> The <see cref="StringComparison"/> that should be used when comparing local file-system paths. </summary>
        public static StringComparison PathStringComparison =>
            IsWindows ? StringComparison.InvariantCultureIgnoreCase : StringComparison.InvariantCulture;

        private static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(SquirrelRuntimeInfo));

        static SquirrelRuntimeInfo()
        {
            EntryExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            BaseDirectory = AppContext.BaseDirectory;

            // if Assembly.Location does not exist, we're almost certainly bundled into a dotnet SingleFile
            // TODO: there is a better way to check this - we can scan the currently executing binary for a
            // SingleFile bundle marker.
            var assyPath = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())?.Location;
            if (String.IsNullOrEmpty(assyPath) || !File.Exists(assyPath))
                IsSingleFile = true;

            // get git/nuget version from nbgv metadata
            SquirrelNugetVersion = NuGetVersion.Parse(ThisAssembly.AssemblyInformationalVersion);
            if (SquirrelNugetVersion.HasMetadata) {
                SquirrelNugetVersion = NuGetVersion.Parse(SquirrelNugetVersion.ToNormalizedString() + "-g" + SquirrelNugetVersion.Metadata);
            }
            SquirrelDisplayVersion = SquirrelNugetVersion.ToNormalizedString() + (SquirrelNugetVersion.IsPrerelease ? " (prerelease)" : "");

            // get real cpu architecture, even when virtualised by Wow64
#if NETFRAMEWORK
            CheckArchitectureWindows();
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                CheckArchitectureWindows();
            } else {
                CheckArchitectureOther();
            }
#endif
        }

        [DllImport("kernel32", EntryPoint = "IsWow64Process2", SetLastError = true)]
        private static extern bool IsWow64Process2(IntPtr hProcess, out ushort pProcessMachine, out ushort pNativeMachine);

        [DllImport("kernel32")]
        private static extern IntPtr GetCurrentProcess();

        private static void CheckArchitectureWindows()
        {
            SystemOsName = "windows";

            // find the actual OS architecture. We can't rely on the framework alone for this on Windows
            // because Wow64 virtualisation is good enough to trick us to believing we're running natively
            // in some cases unless we use functions that are not virtualized (such as IsWow64Process2)

            try {
                if (IsWow64Process2(GetCurrentProcess(), out var _, out var nativeMachine)) {
                    if (Utility.TryParseEnumU16<RuntimeCpu>(nativeMachine, out var val)) {
                        SystemArchitecture = val;
                    }
                }
            } catch {
                // don't care if this function is missing
            }

            if (SystemArchitecture != RuntimeCpu.Unknown) {
                return;
            }

            // https://docs.microsoft.com/en-gb/windows/win32/winprog64/wow64-implementation-details?redirectedfrom=MSDN
            var pf64compat =
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ??
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");

            if (!String.IsNullOrEmpty(pf64compat)) {
                switch (pf64compat) {
                case "ARM64":
                    SystemArchitecture = RuntimeCpu.arm64;
                    break;
                case "AMD64":
                    SystemArchitecture = RuntimeCpu.x64;
                    break;
                }
            }

            if (SystemArchitecture != RuntimeCpu.Unknown) {
                return;
            }

#if NETFRAMEWORK
            SystemArchitecture = Environment.Is64BitOperatingSystem ? RuntimeCpu.x64 : RuntimeCpu.x86;
#else
            CheckArchitectureOther();
#endif
        }

#if !NETFRAMEWORK
        private static void CheckArchitectureOther()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                SystemOsName = "windows";
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                SystemOsName = "linux";
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                SystemOsName = "osx";
            }

            SystemArchitecture = RuntimeInformation.OSArchitecture switch {
                InteropArchitecture.X86 => RuntimeCpu.x86,
                InteropArchitecture.X64 => RuntimeCpu.x64,
                InteropArchitecture.Arm64 => RuntimeCpu.arm64,
                _ => RuntimeCpu.Unknown,
            };
        }
#endif
    }
}