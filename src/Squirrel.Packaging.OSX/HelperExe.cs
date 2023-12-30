using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Squirrel.Packaging.OSX;

public class HelperExe : HelperFile
{
    public HelperExe(ILogger logger) : base(logger)
    {
    }

    public string SquirrelEntitlements => FindHelperFile("Squirrel.entitlements");

    [SupportedOSPlatform("osx")]
    public void CodeSign(string identity, string entitlements, string filePath)
    {
        if (String.IsNullOrEmpty(entitlements)) {
            Log.Info("No codesign entitlements provided, using default dotnet entitlements: " +
                     "https://docs.microsoft.com/en-us/dotnet/core/install/macos-notarization-issues");
            entitlements = SquirrelEntitlements;
        }

        if (!File.Exists(entitlements)) {
            throw new Exception("Could not find entitlements file at: " + entitlements);
        }

        var args = new List<string> {
            "-s", identity,
            "-f",
            "-v",
            "--deep",
            "--timestamp",
            "--options", "runtime",
            "--entitlements", entitlements,
            filePath
        };

        Log.Info($"Beginning codesign for package...");

        Console.WriteLine(InvokeAndThrowIfNonZero("codesign", args, null));

        Log.Info("codesign completed successfully");
    }

    [SupportedOSPlatform("osx")]
    public void SpctlAssessCode(string filePath)
    {
        var args2 = new List<string> {
            "--assess",
            "-vvvv",
            filePath
        };

        Log.Info($"Verifying signature/notarization for code using spctl...");
        Console.WriteLine(InvokeAndThrowIfNonZero("spctl", args2, null));
    }

    [SupportedOSPlatform("osx")]
    public void SpctlAssessInstaller(string filePath)
    {
        var args2 = new List<string> {
            "--assess",
            "-vvv",
            "-t", "install",
            filePath
        };

        Log.Info($"Verifying signature/notarization for installer package using spctl...");
        Console.WriteLine(InvokeAndThrowIfNonZero("spctl", args2, null));
    }

    [SupportedOSPlatform("osx")]
    public void CreateInstallerPkg(string appBundlePath, string appTitle, IEnumerable<KeyValuePair<string, string>> extraContent,
        string pkgOutputPath, string signIdentity)
    {
        // https://matthew-brett.github.io/docosx/flat_packages.html

        Log.Info($"Creating installer '.pkg' for app at '{appBundlePath}'");

        if (File.Exists(pkgOutputPath)) File.Delete(pkgOutputPath);

        using var _1 = Utility.GetTempDirectory(out var tmp);
        using var _2 = Utility.GetTempDirectory(out var tmpPayload1);
        using var _3 = Utility.GetTempDirectory(out var tmpPayload2);
        using var _4 = Utility.GetTempDirectory(out var tmpScripts);
        using var _5 = Utility.GetTempDirectory(out var tmpResources);

        // copy .app to tmp folder
        var bundleName = Path.GetFileName(appBundlePath);
        var tmpBundlePath = Path.Combine(tmpPayload1, bundleName);
        Utility.CopyFiles(new DirectoryInfo(appBundlePath), new DirectoryInfo(tmpBundlePath));

        // create postinstall scripts to open app after install
        // https://stackoverflow.com/questions/35619036/open-app-after-installation-from-pkg-file-in-mac
        var postinstall = Path.Combine(tmpScripts, "postinstall");
        File.WriteAllText(postinstall, $"#!/bin/sh\nsudo -u \"$USER\" open \"$2/{bundleName}/\"\nexit 0");
        ChmodFileAsExecutable(postinstall);

        // generate non-relocatable component pkg. this will be included into a product archive
        var pkgPlistPath = Path.Combine(tmp, "tmp.plist");
        InvokeAndThrowIfNonZero("pkgbuild", new[] { "--analyze", "--root", tmpPayload1, pkgPlistPath }, null);
        InvokeAndThrowIfNonZero("plutil", new[] { "-replace", "BundleIsRelocatable", "-bool", "NO", pkgPlistPath }, null);

        var pkg1Path = Path.Combine(tmpPayload2, "1.pkg");
        string[] args1 = {
            "--root", tmpPayload1,
            "--component-plist", pkgPlistPath,
            "--scripts", tmpScripts,
            "--install-location", "/Applications",
            pkg1Path,
        };

        InvokeAndThrowIfNonZero("pkgbuild", args1, null);

        // create final product package that contains app component
        var distributionPath = Path.Combine(tmp, "distribution.xml");
        InvokeAndThrowIfNonZero("productbuild", new[] { "--synthesize", "--package", pkg1Path, distributionPath }, null);

        // https://developer.apple.com/library/archive/documentation/DeveloperTools/Reference/DistributionDefinitionRef/Chapters/Distribution_XML_Ref.html
        var distXml = File.ReadAllLines(distributionPath).ToList();

        distXml.Insert(2, $"<title>{SecurityElement.Escape(appTitle)}</title>");

        // disable local system installation (install to home dir)
        distXml.Insert(2, "<domains enable_anywhere=\"false\" enable_currentUserHome=\"true\" enable_localSystem=\"false\" />");

        // add extra landing content (eg. license, readme)
        foreach (var kvp in extraContent) {
            if (!String.IsNullOrEmpty(kvp.Value) && File.Exists(kvp.Value)) {
                var fileName = Path.GetFileName(kvp.Value);
                File.Copy(kvp.Value, Path.Combine(tmpResources, fileName));
                distXml.Insert(2, $"<{kvp.Key} file=\"{fileName}\" />");
            }
        }

        File.WriteAllLines(distributionPath, distXml);

        List<string> args2 = new() {
            "--distribution", distributionPath,
            "--package-path", tmpPayload2,
            "--resources", tmpResources,
            pkgOutputPath
        };

        if (!String.IsNullOrEmpty(signIdentity)) {
            args2.Add("--sign");
            args2.Add(signIdentity);
        } else {
            Log.Warn("No Installer signing identity provided. The '.pkg' will not be signed.");
        }

        InvokeAndThrowIfNonZero("productbuild", args2, null);

        Log.Info("Installer created successfully");
    }

    [SupportedOSPlatform("osx")]
    public void Notarize(string filePath, string keychainProfileName)
    {
        Log.Info($"Preparing to Notarize '{filePath}'. This will upload to Apple and usually takes minutes, but could take hours.");

        var args = new List<string> {
            "notarytool",
            "submit",
            "-f", "json",
            "--keychain-profile", keychainProfileName,
            "--wait",
            filePath
        };

        var ntresultjson = InvokeProcess("xcrun", args, null);
        Log.Info(ntresultjson.StdOutput);

        // try to catch any notarization errors. if we have a submission id, retrieve notary logs.
        try {
            var ntresult = JsonConvert.DeserializeObject<NotaryToolResult>(ntresultjson.StdOutput);
            if (ntresult?.status != "Accepted" || ntresultjson.ExitCode != 0) {
                if (ntresult?.id != null) {
                    var logargs = new List<string> {
                        "notarytool",
                        "log",
                        ntresult?.id,
                        "--keychain-profile", keychainProfileName,
                    };

                    var result = InvokeProcess("xcrun", logargs, null);
                    Log.Warn(result.StdOutput);
                }

                throw new Exception("Notarization failed: " + ntresultjson.StdOutput);
            }
        } catch (JsonReaderException) {
            throw new Exception("Notarization failed: " + ntresultjson.StdOutput);
        }

        Log.Info("Notarization completed successfully");
    }

    [SupportedOSPlatform("osx")]
    public void Staple(string filePath)
    {
        Log.Info($"Stapling Notarization to '{filePath}'");
        Console.WriteLine(InvokeAndThrowIfNonZero("xcrun", new[] { "stapler", "staple", filePath }, null));
    }

    private class NotaryToolResult
    {
        public string id { get; set; }
        public string message { get; set; }
        public string status { get; set; }
    }

    [SupportedOSPlatform("osx")]
    public void CreateDittoZip(string folder, string outputZip)
    {
        if (File.Exists(outputZip)) File.Delete(outputZip);

        var args = new List<string> {
            "-c",
            "-k",
            "--rsrc",
            "--keepParent",
            "--sequesterRsrc",
            folder,
            outputZip
        };

        Log.Info($"Creating ditto bundle '{outputZip}'");
        InvokeAndThrowIfNonZero("ditto", args, null);
    }

    private const string OSX_CSTD_LIB = "libSystem.dylib";
    private const string NIX_CSTD_LIB = "libc";

    [SupportedOSPlatform("osx")]
    [DllImport(OSX_CSTD_LIB, EntryPoint = "chmod", SetLastError = true)]
    private static extern int osx_chmod(string pathname, int mode);

    [SupportedOSPlatform("linux")]
    [DllImport(NIX_CSTD_LIB, EntryPoint = "chmod", SetLastError = true)]
    private static extern int nix_chmod(string pathname, int mode);

    protected static void ChmodFileAsExecutable(string filePath)
    {
        Func<string, int, int> chmod;

        if (SquirrelRuntimeInfo.IsOSX) chmod = osx_chmod;
        else if (SquirrelRuntimeInfo.IsLinux) chmod = nix_chmod;
        else return; // no-op on windows, all .exe files can be executed.

        var filePermissionOctal = Convert.ToInt32("777", 8);
        const int EINTR = 4;
        int chmodReturnCode;

        do {
            chmodReturnCode = chmod(filePath, filePermissionOctal);
        } while (chmodReturnCode == -1 && Marshal.GetLastWin32Error() == EINTR);

        if (chmodReturnCode == -1) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not set file permission {filePermissionOctal} for {filePath}.");
        }
    }
}