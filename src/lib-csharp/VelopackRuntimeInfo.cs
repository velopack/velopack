#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NuGet.Versioning;
using Velopack.Util;

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
    internal static class IsExternalInit
    {
    }
}
#endif

namespace Velopack
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

    /// <summary> The Runtime OS </summary>
    public enum RuntimeOs
    {
        /// <summary> Unknown or unsupported </summary>
        Unknown = 0,

        /// <summary> Windows </summary>
        Windows = 1,

        /// <summary> Linux </summary>
        Linux = 2,

        /// <summary> OSX </summary>
        OSX = 3,
    }

    /// <summary>
    /// Convenience class which provides runtime information about the current executing process, 
    /// in a way that is safe in older and newer versions of the framework.
    /// </summary>
    public static class VelopackRuntimeInfo
    {
        /// <summary> The current compiled Velopack display version. </summary>
        public static string VelopackDisplayVersion { get; }

        /// <summary> The current compiled Velopack NuGetVersion. </summary>
        public static NuGetVersion VelopackNugetVersion { get; }

        /// <summary> The current compiled Velopack ProductVersion. </summary>
        public static NuGetVersion VelopackProductVersion { get; }

        /// <summary> The current machine architecture, ignoring the current process / pe architecture. </summary>
        public static RuntimeCpu SystemArch { get; private set; }

        /// <summary> The name of the current OS - eg. 'windows', 'linux', or 'osx'. </summary>
        public static RuntimeOs SystemOs { get; private set; }

        /// <summary> The current system RID. </summary>
        public static string SystemRid => $"{SystemOs.GetOsShortName()}-{SystemArch}";

        /// <summary> True if executing on a Windows platform. </summary>
        [SupportedOSPlatformGuard("windows")]
        public static bool IsWindows => SystemOs == RuntimeOs.Windows;

        /// <summary> True if executing on a Linux platform. </summary>
        [SupportedOSPlatformGuard("linux")]
        public static bool IsLinux => SystemOs == RuntimeOs.Linux;

        /// <summary> True if executing on a MacOS / OSX platform. </summary>
        [SupportedOSPlatformGuard("osx")]
        public static bool IsOSX => SystemOs == RuntimeOs.OSX;

        internal static bool InUnitTestRunner { get; }

        internal static StringComparer PathStringComparer =>
            IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        internal static StringComparison PathStringComparison =>
            IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        static VelopackRuntimeInfo()
        {
#if DEBUG
            InUnitTestRunner = CheckForUnitTestRunner();
#endif

            // get git/nuget version from nbgv metadata
            VelopackProductVersion = NuGetVersion.Parse(ThisAssembly.AssemblyInformationalVersion);

#pragma warning disable CS0162
            if (ThisAssembly.IsPublicRelease) {
                VelopackNugetVersion = NuGetVersion.Parse(NuGetVersion.Parse(ThisAssembly.AssemblyInformationalVersion).ToNormalizedString());
            } else {
                VelopackNugetVersion = NuGetVersion.Parse(ThisAssembly.AssemblyInformationalVersion);
                if (VelopackNugetVersion.HasMetadata) {
                    VelopackNugetVersion = NuGetVersion.Parse(VelopackNugetVersion.ToNormalizedString() + "-g" + VelopackNugetVersion.Metadata);
                }
            }

            VelopackDisplayVersion = VelopackNugetVersion.ToNormalizedString() + (VelopackNugetVersion.IsPrerelease ? " (prerelease)" : "");
#pragma warning restore CS0612

            // get real cpu architecture, even when virtualized by Wow64
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

#if DEBUG
        internal static bool CheckForUnitTestRunner()
        {
            bool searchForAssembly(IEnumerable<string> assemblyList)
            {
                return AppDomain.CurrentDomain.GetAssemblies()
                    .Any(x => assemblyList.Any(name => x.FullName.ToUpperInvariant().Contains(name)));
            }

            var testAssemblies = new[] {
                "CSUNIT",
                "NUNIT",
                "XUNIT",
                "MBUNIT",
                "NBEHAVE",
            };

            try {
                return searchForAssembly(testAssemblies);
            } catch (Exception) {
                return false;
            }
        }
#endif

        /// <summary>
        /// Returns the shortened OS name as a string, suitable for creating an RID.
        /// </summary>
        public static string GetOsShortName(this RuntimeOs os)
        {
            return os switch {
                RuntimeOs.Windows => "win",
                RuntimeOs.Linux => "linux",
                RuntimeOs.OSX => "osx",
                _ => "",
            };
        }

        /// <summary>
        /// Returns the long OS name, suitable for showing to a human.
        /// </summary>
        public static string GetOsLongName(this RuntimeOs os)
        {
            return os switch {
                RuntimeOs.Windows => "Windows",
                RuntimeOs.Linux => "Linux",
                RuntimeOs.OSX => "OSX",
                _ => "",
            };
        }

        [DllImport("kernel32", EntryPoint = "IsWow64Process2", SetLastError = true)]
        private static extern bool IsWow64Process2(IntPtr hProcess, out ushort pProcessMachine, out ushort pNativeMachine);

        [DllImport("kernel32")]
        private static extern IntPtr GetCurrentProcess();

        private static void CheckArchitectureWindows()
        {
            SystemOs = RuntimeOs.Windows;

            // find the actual OS architecture. We can't rely on the framework alone for this on Windows
            // because Wow64 virtualization is good enough to trick us to believing we're running natively
            // in some cases unless we use functions that are not virtualized (such as IsWow64Process2)

            try {
                if (IsWow64Process2(GetCurrentProcess(), out var _, out var nativeMachine)) {
                    if (CoreUtil.TryParseEnumU16<RuntimeCpu>(nativeMachine, out var val)) {
                        SystemArch = val;
                    }
                }
            } catch {
                // don't care if this function is missing
            }

            if (SystemArch != RuntimeCpu.Unknown) {
                return;
            }

            // https://docs.microsoft.com/windows/win32/winprog64/wow64-implementation-details?redirectedfrom=MSDN
            var pf64compat =
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ??
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");

            if (!String.IsNullOrEmpty(pf64compat)) {
                switch (pf64compat) {
                case "ARM64":
                    SystemArch = RuntimeCpu.arm64;
                    break;
                case "AMD64":
                    SystemArch = RuntimeCpu.x64;
                    break;
                }
            }

            if (SystemArch != RuntimeCpu.Unknown) {
                return;
            }

#if NETFRAMEWORK
            SystemArch = Environment.Is64BitOperatingSystem ? RuntimeCpu.x64 : RuntimeCpu.x86;
#else
            CheckArchitectureOther();
#endif
        }

#if !NETFRAMEWORK
        private static void CheckArchitectureOther()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                SystemOs = RuntimeOs.Windows;
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                SystemOs = RuntimeOs.Linux;
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                SystemOs = RuntimeOs.OSX;
            }

            SystemArch = RuntimeInformation.OSArchitecture switch {
                InteropArchitecture.X86 => RuntimeCpu.x86,
                InteropArchitecture.X64 => RuntimeCpu.x64,
                InteropArchitecture.Arm64 => RuntimeCpu.arm64,
                _ => RuntimeCpu.Unknown,
            };
        }
#endif
    }
}