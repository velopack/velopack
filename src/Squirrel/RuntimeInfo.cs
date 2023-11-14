using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using NuGet.Versioning;
using Squirrel.Sources;

namespace Squirrel
{
    public static partial class Runtimes
    {
        /// <summary> Dotnet Runtime SKU </summary>
        public enum DotnetRuntimeType
        {
            /// <summary> The .NET Runtime contains just the components needed to run a console app </summary>
            Runtime = 1,
            /// <summary> The The ASP.NET Core Runtime enables you to run existing web/server applications </summary>
            AspNetCore,
            /// <summary> The .NET Desktop Runtime enables you to run existing Windows desktop applications </summary>
            WindowsDesktop,
        }

        /// <summary> Runtime installation result code </summary>
        public enum RuntimeInstallResult
        {
            /// <summary> The install was successful </summary>
            InstallSuccess = 0,
            /// <summary> The install failed because it was cancelled by the user </summary>
            UserCancelled = 1602,
            /// <summary> The install failed because another install is in progress, try again later </summary>
            AnotherInstallInProgress = 1618,
            /// <summary> The install failed because a system restart is required before continuing </summary>
            RestartRequired = 3010,
            /// <summary> The install failed because the current system does not support this runtime (outdated/unsupported) </summary>
            SystemDoesNotMeetRequirements = 5100,
        }

        /// <summary> Base type containing information about a runtime in relation to the current operating system </summary>
        public abstract class RuntimeInfo
        {
            /// <summary> The unique Id of this runtime. Can be used to retrieve a runtime instance with <see cref="Runtimes.GetRuntimeByName(string)"/> </summary>
            public virtual string Id { get; }

            /// <summary> The human-friendly name of this runtime - for displaying to users </summary>
            public virtual string DisplayName { get; }

            /// <summary> Creates a new instance with the specified properties </summary>
            protected RuntimeInfo() { }

            /// <summary> Creates a new instance with the specified properties </summary>
            protected RuntimeInfo(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }

            /// <summary> Retrieves the web url to the latest compatible runtime installer exe </summary>
            public abstract Task<string> GetDownloadUrl();

            /// <summary> Check if a runtime compatible with the current instance is installed on this system </summary>
            [SupportedOSPlatform("windows")]
            public abstract Task<bool> CheckIsInstalled();

            /// <summary> Check if this runtime is supported on the current system </summary>
            [SupportedOSPlatform("windows")]
            public abstract Task<bool> CheckIsSupported();

            /// <summary> Download the latest installer for this runtime to the specified file </summary>
            public virtual async Task DownloadToFile(string localPath, Action<int> progress = null, IFileDownloader downloader = null)
            {
                var url = await GetDownloadUrl().ConfigureAwait(false);
                Log.Info($"Downloading {Id} from {url} to {localPath}");
                downloader = downloader ?? Utility.CreateDefaultDownloader();
                await downloader.DownloadFile(url, localPath, progress).ConfigureAwait(false);
            }

            /// <summary> Execute a runtime installer at a local file path. Typically used after <see cref="DownloadToFile"/> </summary>
            [SupportedOSPlatform("windows")]
            public virtual async Task<RuntimeInstallResult> InvokeInstaller(string pathToInstaller, bool isQuiet)
            {
                var args = new string[] { "/passive", "/norestart", "/showrmui" };
                var quietArgs = new string[] { "/q", "/norestart" };
                Log.Info($"Running {Id} installer '{pathToInstaller} {string.Join(" ", args)}'");
                var p = await PlatformUtil.InvokeProcessAsync(pathToInstaller, isQuiet ? quietArgs : args, null, CancellationToken.None).ConfigureAwait(false);

                // https://johnkoerner.com/install/windows-installer-error-codes/

                if (p.ExitCode == 1638) // a newer compatible version is already installed
                    return RuntimeInstallResult.InstallSuccess;

                if (p.ExitCode == 1641) // installer initiated a restart
                    return RuntimeInstallResult.RestartRequired;

                return (RuntimeInstallResult) p.ExitCode;
            }

            /// <summary> The unique string representation of this runtime </summary>
            public override string ToString() => $"[{Id}] {DisplayName}";

            /// <summary> The unique hash code of this runtime </summary>
            public override int GetHashCode() => Id.GetHashCode();
        }

        /// <summary> Represents a full .NET Framework runtime, usually included in Windows automatically through Windows Update </summary>
        public class FrameworkInfo : RuntimeInfo
        {
            /// <summary> Permalink to the installer for this runtime </summary>
            public string DownloadUrl { get; }

            /// <summary> The minimum compatible release version for this runtime </summary>
            public int ReleaseVersion { get; }

            private const string ndpPath = "SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full";

            /// <inheritdoc/>
            public FrameworkInfo(string id, string displayName, string downloadUrl, int releaseVersion) : base(id, displayName)
            {
                DownloadUrl = downloadUrl;
                ReleaseVersion = releaseVersion;
            }

            /// <inheritdoc/>
            public override Task<string> GetDownloadUrl()
            {
                return Task.FromResult(DownloadUrl);
            }

            /// <inheritdoc/>
            [SupportedOSPlatform("windows")]
            public override Task<bool> CheckIsSupported()
            {
                // TODO use IsWindowsVersionOrGreater function to verify it can be installed on this machine
                return Task.FromResult(true);
            }

            /// <inheritdoc/>
            [SupportedOSPlatform("windows")]
            public override Task<bool> CheckIsInstalled()
            {
                using var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
                using var key = view.OpenSubKey(ndpPath);
                if (key == null) return Task.FromResult(false);

                var dwRelease = key.GetValue("Release") as int?;
                if (dwRelease == null) return Task.FromResult(false);

                return Task.FromResult(dwRelease.Value >= ReleaseVersion);
            }
        }

        /// <summary> Represents a modern DOTNET runtime where versions are deployed independenly of the operating system </summary>
        public class DotnetInfo : RuntimeInfo
        {
            /// <inheritdoc/>
            public override string Id =>
                 $"{(MinVersion.Major >= 5 ? "net" : "netcoreapp")}{TrimVersion(MinVersion)}-{CpuArchitecture.ToString().ToLower()}-{_runtimeShortForm[RuntimeType]}";

            /// <inheritdoc/>
            public override string DisplayName =>
                 $"{(MinVersion.Major >= 5 ? ".NET" : ".NET Core")} {TrimVersion(MinVersion)} {RuntimeType} ({CpuArchitecture.ToString().ToLower()})";

            /// <summary> The minimum compatible version that must be installed. </summary>
            public NuGetVersion MinVersion { get; }

            /// <summary> The CPU architecture of the runtime. This must match the RID of the app being deployed.
            /// For example, if the Squirrel app was deployed with 'win-x64', this must be X64 also. </summary>
            public RuntimeCpu CpuArchitecture { get; }

            /// <summary> The type of runtime required, eg. Windows Desktop, AspNetCore, Sdk.</summary>
            public DotnetRuntimeType RuntimeType { get; }

            private static readonly Dictionary<DotnetRuntimeType, string> _runtimeShortForm = new() {
                { DotnetRuntimeType.Runtime, "runtime" },
                { DotnetRuntimeType.WindowsDesktop, "desktop" },
                { DotnetRuntimeType.AspNetCore, "asp" },
            };

            /// <inheritdoc/>
            protected DotnetInfo(Version minversion, RuntimeCpu architecture, DotnetRuntimeType runtimeType = DotnetRuntimeType.WindowsDesktop)
            {
                MinVersion = new NuGetVersion(minversion);
                CpuArchitecture = architecture;
                RuntimeType = runtimeType;
                if (minversion.Major == 6 && minversion.Build < 0) {
                    Log.Warn(
                        $"Automatically upgrading minimum dotnet version from net{minversion} to net6.0.2, " +
                        $"see more at https://github.com/dotnet/core/issues/7176. " +
                        $"If you would like to stop this behavior, please specify '--framework net6.0.0'");
                    MinVersion = new NuGetVersion(6, 0, 2);
                }
            }

            internal DotnetInfo(string minversion, RuntimeCpu architecture, DotnetRuntimeType runtimeType = DotnetRuntimeType.WindowsDesktop)
                : this(ParseVersion(minversion), architecture, runtimeType)
            {
            }

            private const string UncachedDotNetFeed = "https://dotnetcli.blob.core.windows.net/dotnet";
            private const string DotNetFeed = "https://dotnetcli.azureedge.net/dotnet";

            /// <inheritdoc/>
            [SupportedOSPlatform("windows")]
            public override Task<bool> CheckIsInstalled()
            {
                var versionDir = GetDotnetVersionDir(CpuArchitecture, RuntimeType);
                if (!Directory.Exists(versionDir))
                    return Task.FromResult(false);

                var dirs = Directory.EnumerateDirectories(versionDir)
                    .Select(d => Path.GetFileName(d))
                    .Where(d => NuGetVersion.TryParse(d, out var _))
                    .Select(d => NuGetVersion.Parse(d));

                var foundCompatibleVer = dirs.Any(v => v.Major == MinVersion.Major && v.Minor == MinVersion.Minor && v >= MinVersion);
                return Task.FromResult(foundCompatibleVer);
            }

            /// <inheritdoc/>
            [SupportedOSPlatform("windows")]
            public override Task<bool> CheckIsSupported()
            {
                // TODO use IsWindowsVersionOrGreater function to verify it can be installed on this machine

                // arm64 windows supports everything
                if (SquirrelRuntimeInfo.SystemArch == RuntimeCpu.arm64)
                    return Task.FromResult(true);

                // if the desired architecture is same as system
                if (SquirrelRuntimeInfo.SystemArch == CpuArchitecture)
                    return Task.FromResult(true);

                // x64 also supports x86
                if (SquirrelRuntimeInfo.SystemArch == RuntimeCpu.x64 && CpuArchitecture == RuntimeCpu.x86)
                    return Task.FromResult(true);

                return Task.FromResult(false);
            }

            [SupportedOSPlatform("windows")]
            private static string GetDotnetVersionDir(RuntimeCpu runtimeArch, DotnetRuntimeType runtimeType)
            {
                var baseDir = GetDotnetBaseDir(runtimeArch);
                if (String.IsNullOrEmpty(baseDir))
                    return null;

                return runtimeType switch {
                    DotnetRuntimeType.Runtime => Path.Combine(baseDir, "shared", "Microsoft.NETCore.App"),
                    DotnetRuntimeType.AspNetCore => Path.Combine(baseDir, "shared", "Microsoft.AspNetCore.App"),
                    DotnetRuntimeType.WindowsDesktop => Path.Combine(baseDir, "shared", "Microsoft.WindowsDesktop.App"),
                    _ => throw new ArgumentOutOfRangeException(nameof(DotnetRuntimeType)),
                };
            }

            [SupportedOSPlatform("windows")]
            private static string GetDotnetBaseDir(RuntimeCpu runtime)
            {
                var system = SquirrelRuntimeInfo.SystemArch;
                var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                if (runtime == RuntimeCpu.x86)
                    return Path.Combine(pf86, "dotnet");

                // this only works in a 64 bit process, otherwise it points to ProgramFilesX86
                var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                // try to get the real 64 bit program files directory
                var pf64compat = Environment.GetEnvironmentVariable("ProgramW6432");
                if (Directory.Exists(pf64compat))
                    pf64 = pf64compat;

                if (runtime == system) {
                    // looking for x64 on an x64 system will always be in pf64.
                    // it's the same when looking for arm64 on an arm64 system
                    return Path.Combine(pf64, "dotnet");
                } else if (runtime == RuntimeCpu.x64 && system == RuntimeCpu.arm64) {
                    // if looking for x64 on an arm64 system, it will be in a sub-directory
                    return Path.Combine(pf64, "dotnet", "x64");
                }

                return null;
            }

            /// <inheritdoc/>
            public override async Task<string> GetDownloadUrl()
            {
                // Note that GetLatestDotNetVersion should still be fixed for WindowsDesktop as it is the only url that the azure
                // blob responds with the latest version. This doesn't matter as all dotnet needed runtimes will have the same latest version.
                var latest = await GetLatestDotNetVersion(DotnetRuntimeType.WindowsDesktop, $"{MinVersion.Major}.{MinVersion.Minor}").ConfigureAwait(false);
                var architecture = CpuArchitecture switch {
                    RuntimeCpu.x86 => "x86",
                    RuntimeCpu.x64 => "x64",
                    RuntimeCpu.arm64 => "arm64",
                    _ => throw new ArgumentOutOfRangeException(nameof(CpuArchitecture)),
                };

                return GetDotNetDownloadUrl(RuntimeType, latest, architecture);
            }

            private static Regex _dotnetRegex = new Regex(@"^net(?:coreapp)?(?<version>[\d\.]{1,7})(?:-(?<arch>[a-zA-Z]+\d\d))?(?:-(?<type>[a-zA-Z]+))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            /// <summary>
            /// Parses a string such as 'net6' or net5.0.14-x86 into a DotnetInfo class capable of checking
            /// the current system for installed status, or downloading / installing.
            /// </summary>
            public static DotnetInfo Parse(string input)
            {
                var match = _dotnetRegex.Match(input);
                if (!match.Success)
                    throw new ArgumentException("Not a valid runtime identifier.", nameof(input));

                var verstr = match.Groups["version"].Value;
                var archstr = match.Groups["arch"].Value; // default is x64 if not specified
                var typestr = match.Groups["type"].Value; // default is WindowsDesktop

                var archValid = Enum.TryParse<RuntimeCpu>(String.IsNullOrWhiteSpace(archstr) ? "x64" : archstr, true, out var cpu);
                if (!archValid)
                    throw new ArgumentException($"Invalid machine architecture '{archstr}'. Valid values: {String.Join(", ", Enum.GetValues(typeof(RuntimeCpu)))}");

                var type = DotnetRuntimeType.WindowsDesktop;
                if (!String.IsNullOrEmpty(typestr)) {
                    var q = _runtimeShortForm.Where(kvp => kvp.Value.Equals(typestr, StringComparison.InvariantCultureIgnoreCase));
                    if (Enum.TryParse<DotnetRuntimeType>(typestr, true, out var parsed)) {
                        type = parsed;
                    } else if (q.Any()) {
                        type = q.First().Key;
                    } else {
                        throw new ArgumentException($"Invalid dotnet runtime sku '{typestr}'. Valid values: {String.Join(", ", _runtimeShortForm.Values)}");
                    }
                }

                var ver = ParseVersion(verstr);
                return new DotnetInfo(ver, cpu, type);
            }

            /// <inheritdoc cref="Parse(string)"/>
            public static bool TryParse(string input, out DotnetInfo info)
            {
                try {
                    info = Parse(input);
                    return true;
                } catch {
                    info = null;
                    return false;
                }
            }

            /// <summary>
            /// Safely converts a version string into a version structure, and provides some validation for invalid/unsupported versions.
            /// </summary>
            protected static Version ParseVersion(string input)
            {
                // Version will not parse "6" by itself, so we add an extra zero to help it out.
                if (!input.Contains("."))
                    input += ".0";

                if (Version.TryParse(input, out var v)) {
                    if (v.Revision > 0)
                        throw new ArgumentException("Version must only be a 3-part version string.", nameof(input));

                    if ((v.Major == 3 && v.Minor == 1) || v.Major >= 5) {
                        return v;
                    }
                    throw new ArgumentException($"Version must be 3.1 or >= 5.0. (Actual: {v})", nameof(input));
                }
                throw new ArgumentException("Invalid version string: " + input, nameof(input));
            }

            /// <summary>
            /// Converts a version structure into the shortest string possible, by trimming trailing zeros.
            /// </summary>
            protected static string TrimVersion(NuGetVersion ver)
            {
                string v = ver.Major.ToString();
                if (ver.Minor > 0 || ver.Patch > 0 || ver.Revision > 0) {
                    v += "." + ver.Minor;
                }
                if (ver.Patch > 0 || ver.Revision > 0) {
                    v += "." + ver.Patch;
                }
                if (ver.Revision > 0) {
                    v += "." + ver.Revision;
                }
                return v;
            }

            /// <summary>
            /// Get latest available version of dotnet. Channel can be 'LTS', 'current', or a two part version 
            /// (eg. '6.0') to get the latest minor release.
            /// </summary>
            public static async Task<string> GetLatestDotNetVersion(DotnetRuntimeType runtimeType, string channel, IFileDownloader downloader = null)
            {
                // https://github.com/dotnet/install-scripts/blob/main/src/dotnet-install.ps1#L427
                // these are case sensitive
                string runtime = runtimeType switch {
                    DotnetRuntimeType.Runtime => "dotnet",
                    DotnetRuntimeType.AspNetCore => "aspnetcore",
                    DotnetRuntimeType.WindowsDesktop => "WindowsDesktop",
                    _ => throw new NotImplementedException(),
                };

                downloader = downloader ?? Utility.CreateDefaultDownloader();

                try {
                    return await downloader.DownloadString($"{UncachedDotNetFeed}/{runtime}/{channel}/latest.version").ConfigureAwait(false);
                } catch (System.Net.Http.HttpRequestException ex) {
                    throw new Exception($"Dotnet version '{channel}' ({runtime}) was not found online or could not be retrieved.", ex);
                }
            }

            /// <summary>
            /// Get download url for a specific version of dotnet. Version must be an absolute version, such as one
            /// returned by <see cref="GetLatestDotNetVersion(DotnetRuntimeType, string, IFileDownloader)"/>. cpuarch should be either
            /// 'x86', 'x64', or 'arm64'.
            /// </summary>
            public static string GetDotNetDownloadUrl(DotnetRuntimeType runtimeType, string version, string cpuarch)
            {
                // https://github.com/dotnet/install-scripts/blob/main/src/dotnet-install.ps1#L619
                return runtimeType switch {
                    DotnetRuntimeType.Runtime => $"{DotNetFeed}/Runtime/{version}/dotnet-runtime-{version}-win-{cpuarch}.exe",
                    DotnetRuntimeType.AspNetCore => $"{DotNetFeed}/aspnetcore/Runtime/{version}/aspnetcore-runtime-{version}-win-{cpuarch}.exe",
                    DotnetRuntimeType.WindowsDesktop =>
                        new Version(version).Major >= 5
                            ? $"{DotNetFeed}/WindowsDesktop/{version}/windowsdesktop-runtime-{version}-win-{cpuarch}.exe"
                            : $"{DotNetFeed}/Runtime/{version}/windowsdesktop-runtime-{version}-win-{cpuarch}.exe",
                    _ => throw new NotImplementedException(),
                };
            }
        }

        /// <summary> The base class for a VC++ redistributable package. </summary>
        public abstract class VCRedistInfo : RuntimeInfo
        {
            /// <summary> The minimum compatible version that must be installed. </summary>
            public NuGetVersion MinVersion { get; }

            /// <summary> The CPU architecture of the runtime. </summary>
            public RuntimeCpu CpuArchitecture { get; }

            /// <inheritdoc/>
            public VCRedistInfo(string id, string displayName, NuGetVersion minVersion, RuntimeCpu cpuArchitecture) : base(id, displayName)
            {
                MinVersion = minVersion;
                CpuArchitecture = cpuArchitecture;
            }

            /// <inheritdoc/>
            [SupportedOSPlatform("windows")]
            public override Task<bool> CheckIsInstalled()
            {
                return Task.FromResult(GetInstalledVCVersions().Any(
                    v => v.Cpu == CpuArchitecture &&
                    v.Ver.Major == MinVersion.Major &&
                    v.Ver >= MinVersion));
            }

            /// <inheritdoc/>
            [SupportedOSPlatform("windows")]
            public override Task<bool> CheckIsSupported()
            {
                // TODO use IsWindowsVersionOrGreater function to verify it can be installed on this machine

                // arm64 windows supports everything
                if (SquirrelRuntimeInfo.SystemArch == RuntimeCpu.arm64)
                    return Task.FromResult(true);

                // if the desired architecture is same as system
                if (SquirrelRuntimeInfo.SystemArch == CpuArchitecture)
                    return Task.FromResult(true);

                // x64 also supports x86
                if (SquirrelRuntimeInfo.SystemArch == RuntimeCpu.x64 && CpuArchitecture == RuntimeCpu.x86)
                    return Task.FromResult(true);

                return Task.FromResult(false);
            }

            const string UninstallRegSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

            private class VCVersion
            {
                public SemanticVersion Ver { get; set; }
                public RuntimeCpu Cpu { get; set; }

                public VCVersion(SemanticVersion ver, RuntimeCpu cpu)
                {
                    Ver = ver;
                    Cpu = cpu;
                }
            }

            /// <summary>
            /// Returns the list of currently installed VC++ redistributables, as reported by the
            /// Windows Programs &amp; Features dialog.
            /// </summary>
            [SupportedOSPlatform("windows")]
            private static VCVersion[] GetInstalledVCVersions()
            {
                List<VCVersion> results = new();

                void searchreg(RegistryKey view)
                {
                    foreach (var kn in view.GetSubKeyNames()) {
                        var subKey = view.OpenSubKey(kn);
                        var name = subKey.GetValue("DisplayName") as string;
                        if (name != null && name.Contains("Microsoft Visual C++") && name.Contains("Redistributable")) {
                            var version = subKey.GetValue("DisplayVersion") as string;
                            if (NuGetVersion.TryParse(version, out var v)) {
                                if (name.IndexOf("arm64", StringComparison.InvariantCultureIgnoreCase) >= 0) {
                                    results.Add(new VCVersion(v, RuntimeCpu.arm64));
                                } else if (name.IndexOf("x64", StringComparison.InvariantCultureIgnoreCase) >= 0) {
                                    results.Add(new VCVersion(v, RuntimeCpu.x64));
                                } else {
                                    results.Add(new VCVersion(v, RuntimeCpu.x86));
                                }
                            }
                        }
                    }
                }

                using var view86 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                   .CreateSubKey(UninstallRegSubKey, RegistryKeyPermissionCheck.ReadSubTree);
                searchreg(view86);

                if (Environment.Is64BitOperatingSystem) {
                    using var view64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                        .CreateSubKey(UninstallRegSubKey, RegistryKeyPermissionCheck.ReadSubTree);
                    searchreg(view64);
                }

                return results.OrderBy(v => v.Ver).ToArray();
            }
        }

        /// <summary> Represents a VC++ 2015-2022 redistributable package. </summary>
        public class VCRedist14 : VCRedistInfo
        {
            /// <inheritdoc/>
            public VCRedist14(string id, string displayName, NuGetVersion minVersion, RuntimeCpu cpuArchitecture)
                : base(id, displayName, minVersion, cpuArchitecture)
            {
            }

            /// <inheritdoc/>
            public override Task<string> GetDownloadUrl()
            {
                // from 2015-2022, the binaries are all compatible, so we can always just install the latest version
                // https://docs.microsoft.com/en-US/cpp/windows/latest-supported-vc-redist?view=msvc-170#visual-studio-2015-2017-2019-and-2022
                // https://docs.microsoft.com/en-us/cpp/porting/binary-compat-2015-2017?view=msvc-170
                return Task.FromResult(CpuArchitecture switch {
                    RuntimeCpu.x86 => "https://aka.ms/vs/17/release/vc_redist.x86.exe",
                    RuntimeCpu.x64 => "https://aka.ms/vs/17/release/vc_redist.x64.exe",
                    RuntimeCpu.arm64 => "https://aka.ms/vs/17/release/vc_redist.arm64.exe",
                    _ => throw new ArgumentOutOfRangeException(nameof(CpuArchitecture)),
                });
            }
        }

        /// <summary> Represents a VC++ redistributable package which is referenced by a permalink </summary>
        public class VCRedist00 : VCRedistInfo
        {
            /// <summary> Permalink to the installer for this runtime </summary>
            public string DownloadUrl { get; }

            /// <inheritdoc/>
            public VCRedist00(string id, string displayName, NuGetVersion minVersion, RuntimeCpu cpuArchitecture, string downloadUrl)
                : base(id, displayName, minVersion, cpuArchitecture)
            {
                DownloadUrl = downloadUrl;
            }

            /// <inheritdoc/>
            public override Task<string> GetDownloadUrl() => Task.FromResult(DownloadUrl);
        }
    }
}
