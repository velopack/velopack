using System.Runtime.Versioning;
using System.Security;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Unix;

[SupportedOSPlatform("osx")]
public class OsxBuildTools
{
    public const string PlistBuddy = "/usr/libexec/PlistBuddy";

    public ILogger Log { get; }

    public OsxBuildTools(ILogger logger)
    {
        Log = logger;
    }

    /// <summary>
    /// Reads a top-level string value from a (binary or XML) plist with PlistBuddy. Returns null when the key is
    /// absent (non-zero exit). For a dict/array key the printed structure is returned.
    /// </summary>
    public static string ReadPlistValue(string plistPath, string key)
    {
        var result = Exe.InvokeProcess(PlistBuddy, new[] { "-c", $"Print :{key}", plistPath }, null);
        return result.ExitCode == 0 ? result.StdOutput : null;
    }

    public void CodeSign(string identity, string entitlements, string filePath, bool deep, string keychainPath)
    {
        if (!File.Exists(entitlements)) {
            throw new Exception("Could not find entitlements file at: " + entitlements);
        }

        var args = new List<string> {
            "-s", identity,
            "-f",
            "-v",
            "--timestamp",
            "--options", "runtime",
            "--entitlements", entitlements,
        };

        if (deep) {
            args.Add("--deep");
        }

        if (!String.IsNullOrEmpty(keychainPath)) {
            Log.Debug($"Using non-default keychain at '{keychainPath}'");
            args.Add("--keychain");
            args.Add(keychainPath);
        }

        args.Add(filePath);

        Log.Debug($"Beginning codesign for package...");
        string output = Exe.InvokeAndThrowIfNonZero("codesign", args, null);
        if (!String.IsNullOrWhiteSpace(output)) {
            Log.Info(output);
        }

        Log.Debug("codesign completed successfully");
    }

    public void SpctlAssessCode(string filePath)
    {
        var args2 = new List<string> {
            "--assess",
            "-vvvv",
            filePath
        };

        Log.Info($"Verifying signature/notarization for code using spctl...");
        Log.Info(Exe.InvokeAndThrowIfNonZero("spctl", args2, null));
    }

    public void SpctlAssessInstaller(string filePath)
    {
        var args2 = new List<string> {
            "--assess",
            "-vvv",
            "-t", "install",
            filePath
        };

        Log.Info($"Verifying signature/notarization for installer package using spctl...");
        Log.Info(Exe.InvokeAndThrowIfNonZero("spctl", args2, null));
    }

    public void CreateInstallerPkg(string appBundlePath, string appTitle, string appId, IEnumerable<KeyValuePair<string, string>> extraContent,
        string pkgOutputPath, string signIdentity, Action<int> progress)
    {
        // https://matthew-brett.github.io/docosx/flat_packages.html

        Log.Info($"Creating installer '.pkg' for app at '{appBundlePath}'");

        if (File.Exists(pkgOutputPath)) File.Delete(pkgOutputPath);

        using var _1 = TempUtil.GetTempDirectory(out var tmp);
        using var _2 = TempUtil.GetTempDirectory(out var tmpPayload1);
        using var _3 = TempUtil.GetTempDirectory(out var tmpPayload2);
        using var _4 = TempUtil.GetTempDirectory(out var tmpScripts);
        using var _5 = TempUtil.GetTempDirectory(out var tmpResources);

        // copy .app to tmp folder
        var bundleName = Path.GetFileName(appBundlePath);
        var tmpBundlePath = Path.Combine(tmpPayload1, bundleName);
        FileUtil.CopyDirectoryContents(appBundlePath, tmpBundlePath, Log);
        progress(10);

        // create postinstall scripts to open app after install
        // https://stackoverflow.com/questions/35619036/open-app-after-installation-from-pkg-file-in-mac
        // the association helper bundle (Contents/Helpers/*.app) is registered with LaunchServices as the console
        // user, because a registration done as root would land in root's LS database.
        var postinstall = Path.Combine(tmpScripts, "postinstall");
        File.WriteAllText(
            postinstall,
            $"""
            #!/bin/sh
            rm -rf /tmp/velopack/{appId}
            sudo -u "$USER" rm -rf ~/Library/Caches/velopack/{appId}
            LSREG=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
            if ls "$2/{bundleName}/Contents/Helpers/"*.app >/dev/null 2>&1; then
              sudo -u "$USER" "$LSREG" -f "$2/{bundleName}"
              for h in "$2/{bundleName}/Contents/Helpers/"*.app; do sudo -u "$USER" "$LSREG" -f "$h"; done
            fi
            sudo -u "$USER" env VELOPACK_FIRSTRUN=1 open "$2/{bundleName}/"
            exit 0
            """);
        Chmod.ChmodFileAsExecutable(postinstall);
        progress(15);

        // generate non-relocatable component pkg. this will be included into a product archive.
        // nested bundles (the association helper) are listed under ChildBundles with their own flag; without it
        // Installer may "relocate" the helper onto a stale copy of a bundle with the same id found elsewhere.
        var pkgPlistPath = Path.Combine(tmp, "tmp.plist");
        Exe.InvokeAndThrowIfNonZero("pkgbuild", new[] { "--analyze", "--root", tmpPayload1, pkgPlistPath }, null);
        Exe.InvokeAndThrowIfNonZero(PlistBuddy, new[] { "-c", "Set :0:BundleIsRelocatable false", pkgPlistPath }, null);
        for (int n = 0;; n++) {
            var child = Exe.InvokeProcess(PlistBuddy, new[] { "-c", $"Set :0:ChildBundles:{n}:BundleIsRelocatable false", pkgPlistPath }, null);
            if (child.ExitCode != 0) {
                break; // no more child bundles
            }

            Log.Debug($"Marked child bundle {n} as not relocatable.");
        }

        progress(50);

        var pkg1Path = Path.Combine(tmpPayload2, "1.pkg");
        string[] args1 = {
            "--root", tmpPayload1,
            "--component-plist", pkgPlistPath,
            "--scripts", tmpScripts,
            "--install-location", "/Applications",
            pkg1Path,
        };

        Exe.InvokeAndThrowIfNonZero("pkgbuild", args1, null);
        progress(70);

        // create final product package that contains app component
        var distributionPath = Path.Combine(tmp, "distribution.xml");
        Exe.InvokeAndThrowIfNonZero("productbuild", new[] { "--synthesize", "--package", pkg1Path, distributionPath }, null);
        progress(80);

        // https://developer.apple.com/library/archive/documentation/DeveloperTools/Reference/DistributionDefinitionRef/Chapters/Distribution_XML_Ref.html
        var distXml = File.ReadAllLines(distributionPath).ToList();

        distXml.Insert(2, $"<title>{SecurityElement.Escape(appTitle)}</title>");

        // disable local system installation (install to home dir)
        distXml.Insert(2, "<domains enable_anywhere=\"false\" enable_currentUserHome=\"true\" enable_localSystem=\"true\" />");

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

        Exe.InvokeAndThrowIfNonZero("productbuild", args2, null);
        progress(100);

        Log.Info("Installer created successfully");
    }

    public void Notarize(string filePath, string keychainProfileName, string keychainPath)
    {
        Log.Info($"Preparing to Notarize. This will upload to Apple and usually takes minutes, [underline]but could take hours.[/]");

        var args = new List<string> {
            "notarytool",
            "submit",
            "-f", "json",
            "--wait",
            "--keychain-profile", keychainProfileName,
        };

        if (!String.IsNullOrEmpty(keychainPath)) {
            Log.Info($"Using non-default keychain at '{keychainPath}'");
            args.Add("--keychain");
            args.Add(keychainPath);
        }

        args.Add(filePath);

        var ntresultjson = Exe.InvokeProcess("xcrun", args, null);
        if (!String.IsNullOrEmpty(ntresultjson.StdErr)) {
            Log.Warn(ntresultjson.StdErr);
        }

        // if the keychain/credentials are missing or invalid, the notarization will fail with an empty output.
        if (String.IsNullOrEmpty(ntresultjson.StdOutput)) {
            throw new UserInfoException(
                $"Notarization failed. 'xcrun notarytool submit' with exit code {ntresultjson.ExitCode} and empty output.");
        }

        // try to catch any notarization errors. if we have a submission id, retrieve and display notary logs.
        try {
            var ntresult = SimpleJson.DeserializeObject<NotaryToolResult>(ntresultjson.StdOutput);
            if (ntresult?.status != "Accepted" || ntresultjson.ExitCode != 0) {
                if (ntresult?.id != null) {
                    var logargs = new List<string> {
                        "notarytool",
                        "log",
                        ntresult?.id,
                        "--keychain-profile", keychainProfileName,
                    };

                    var result = Exe.InvokeProcess("xcrun", logargs, null);
                    Log.Warn(result.StdOutput);
                }

                throw new UserInfoException(
                    $"Notarization failed. 'xcrun notarytool submit' with exit code {ntresultjson.ExitCode} and output:{Environment.NewLine}" +
                    ntresultjson.StdOutput);
            }
        } catch (Exception ex) when (ex is not UserInfoException) {
            Log.Warn(ex, "Failed to parse notarization result.");
            throw new UserInfoException(
                $"Notarization failed. 'xcrun notarytool submit' with exit code {ntresultjson.ExitCode} and output:{Environment.NewLine}" +
                ntresultjson.StdOutput);
        }

        Log.Info(ntresultjson.StdOutput);
        Log.Info("Notarization completed successfully");
    }

    public void Staple(string filePath)
    {
        Log.Debug($"Stapling Notarization to '{filePath}'");
        Log.Info(Exe.InvokeAndThrowIfNonZero("xcrun", new[] { "stapler", "staple", filePath }, null));
    }

    private class NotaryToolResult
    {
        public string id { get; set; }
        public string message { get; set; }
        public string status { get; set; }
    }

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

        Log.Debug($"Creating ditto bundle '{outputZip}'");
        Log.Debug(Exe.InvokeAndThrowIfNonZero("ditto", args, null));
    }

    public string ExtractPkgToAppBundle(string pkgFile, string extractionTmpPath)
    {
        if (!File.Exists(pkgFile)) {
            throw new ArgumentException("Package file does not exist: " + pkgFile);
        }

        Log.Debug($"Extracting '{pkgFile}' to '{extractionTmpPath}'");

        var args = new List<string> {
            "--expand-full",
            pkgFile,
            extractionTmpPath,
        };

        Log.Debug(Exe.InvokeAndThrowIfNonZero("pkgutil", args, null));

        // only top-level bundles count: a nested Contents/Helpers/*.app (the association helper) is part of its parent
        var appPaths = Directory.EnumerateDirectories(extractionTmpPath, "*.app", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(extractionTmpPath, Path.GetDirectoryName(p))
                .Split(Path.DirectorySeparatorChar)
                .Any(s => s.EndsWith(".app", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (appPaths.Length > 1) {
            throw new Exception("The package contains more than one .app bundle. This is not supported.");
        }

        if (!appPaths.Any()) {
            throw new Exception("The package does not contain an .app bundle.");
        }

        return appPaths.First();
    }
}