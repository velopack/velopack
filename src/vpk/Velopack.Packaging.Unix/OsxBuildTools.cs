using System.Runtime.Versioning;
using System.Security;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Unix;

[SupportedOSPlatform("osx")]
public class OsxBuildTools
{
    public ILogger Log { get; }

    public OsxBuildTools(ILogger logger)
    {
        Log = logger;
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
            Log.Info($"Using non-default keychain at '{keychainPath}'");
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

    public void CopyPreserveSymlinks(string source, string dest)
    {
        if (!Directory.Exists(source)) {
            throw new ArgumentException("Source directory does not exist: " + source);
        }
        Log.Debug($"Copying '{source}' to '{dest}' (preserving symlinks)");

        // copy the contents of the folder, not the folder itself.
        var src = source.TrimEnd('/') + "/.";
        var des = dest.TrimEnd('/') + "/";
        Log.Debug(Exe.InvokeAndThrowIfNonZero("cp", new[] { "-a", src, des }, null));
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
        CopyPreserveSymlinks(appBundlePath, tmpBundlePath);
        progress(10);

        // create postinstall scripts to open app after install
        // https://stackoverflow.com/questions/35619036/open-app-after-installation-from-pkg-file-in-mac
        var postinstall = Path.Combine(tmpScripts, "postinstall");
        File.WriteAllText(postinstall, $"""
#!/bin/sh
rm -rf /tmp/velopack/{appId}
sudo -u "$USER" rm -rf ~/Library/Caches/velopack/{appId}
sudo -u "$USER" env VELOPACK_FIRSTRUN=1 open "$2/{bundleName}/"
exit 0
""");
        Chmod.ChmodFileAsExecutable(postinstall);
        progress(15);

        // generate non-relocatable component pkg. this will be included into a product archive
        var pkgPlistPath = Path.Combine(tmp, "tmp.plist");
        Exe.InvokeAndThrowIfNonZero("pkgbuild", new[] { "--analyze", "--root", tmpPayload1, pkgPlistPath }, null);
        Exe.InvokeAndThrowIfNonZero("plutil", new[] { "-replace", "BundleIsRelocatable", "-bool", "NO", pkgPlistPath }, null);
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
        Log.Info(ntresultjson.StdOutput);

        // try to catch any notarization errors. if we have a submission id, retrieve notary logs.
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

                throw new Exception("Notarization failed: " + ntresultjson.StdOutput);
            }
        } catch (Exception ex) {
            throw new Exception("Notarization failed: " + ntresultjson.StdOutput + Environment.NewLine + ex.Message);
        }

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
        
        IEnumerable<string> appPaths = Directory.EnumerateDirectories(extractionTmpPath, "*.app", SearchOption.AllDirectories).ToArray();

        if (appPaths.Count() > 1) {
            throw new Exception("The package contains more than one .app bundle. This is not supported.");
        }
        
        if (!appPaths.Any()) {
            throw new Exception("The package does not contain an .app bundle.");
        }
        
        return appPaths.First();
    }
}