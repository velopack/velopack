using Microsoft.Extensions.Logging;
using Velopack.Core;

namespace Velopack.Packaging.Unix;

/// <summary>
/// Signs and notarizes macOS bundles with rcodesign (apple-platform-rs), which implements Apple's code signature format
/// and the notary service's API in portable Rust. It is what lets <c>vpk [osx] pack</c> produce a signed, notarized,
/// stapled release on Linux, where Apple's own codesign / notarytool / stapler do not exist. It also runs on macOS.
///
/// It is selected by passing a certificate file (--signP12File) rather than a keychain identity (--signAppIdentity):
/// rcodesign never touches the keychain, so the certificate and its password travel as files, which suits CI.
/// https://gregoryszorc.com/docs/apple-codesign/stable/
/// </summary>
public class RcodesignTools
{
    private const string Binary = "rcodesign";

    public ILogger Log { get; }

    public RcodesignTools(ILogger logger)
    {
        Log = logger;
    }

    public static void AssertInstalled()
    {
        Exe.AssertSystemBinaryExists(Binary, "cargo install apple-codesign", "cargo install apple-codesign");
    }

    /// <summary>
    /// Signs an .app bundle with the hardened runtime and a secure timestamp (rcodesign timestamps with Apple's server by
    /// default), with <paramref name="appEntitlements"/> on the main executable. The same settings the codesign route
    /// uses, and deliberately not rcodesign's --for-notarization, which additionally refuses any certificate that is not
    /// an Apple-issued Developer ID: notarization enforces that anyway, and the check would stop a test certificate
    /// from signing at all, which codesign allows.
    ///
    /// A bundle signature in rcodesign is always deep: it signs every nested bundle and every Mach-O in Contents/MacOS
    /// before sealing the bundle, as `codesign --deep` does. <paramref name="scopedEntitlements"/> gives individual
    /// nested binaries (bundle-relative paths) their own entitlements instead, which is how UpdateMac keeps Velopack's.
    /// </summary>
    public void SignBundle(string bundlePath, string p12File, string p12PasswordFile, string appEntitlements,
        IReadOnlyDictionary<string, string> scopedEntitlements = null)
    {
        var args = new List<string> {
            "sign",
            "--code-signature-flags", "runtime",
            "--p12-file", p12File,
            "--p12-password-file", p12PasswordFile,
            // UNSCOPED, which applies to the bundle's main executable. Not "main:", which rcodesign's help describes as
            // the main entity and everything nested, but which in practice (0.29) left the main executable with no
            // entitlements at all: a .NET app then signs, notarizes, and dies at its first JIT.
            "--entitlements-xml-file", appEntitlements,
        };

        if (scopedEntitlements != null) {
            foreach (var (relativePath, entitlements) in scopedEntitlements) {
                args.Add("--entitlements-xml-file");
                args.Add(relativePath + ":" + entitlements);
            }
        }

        args.Add(bundlePath);

        Log.Debug($"Signing '{bundlePath}' with rcodesign...");
        var output = Exe.InvokeAndThrowIfNonZero(Binary, args, null);
        if (!String.IsNullOrWhiteSpace(output)) {
            Log.Debug(output);
        }
    }

    /// <summary>
    /// Signs a single Mach-O file, for --signDisableDeep: the caller has signed everything else already and only wants
    /// what Velopack added (UpdateMac) signed before the bundle is sealed with <see cref="SignBundleShallow"/>.
    /// </summary>
    public void SignFile(string filePath, string p12File, string p12PasswordFile, string entitlements)
    {
        var args = new List<string> {
            "sign",
            "--code-signature-flags", "runtime",
            "--p12-file", p12File,
            "--p12-password-file", p12PasswordFile,
            "--entitlements-xml-file", entitlements,
            filePath,
        };

        Log.Debug($"Signing '{filePath}' with rcodesign...");
        Exe.InvokeAndThrowIfNonZero(Binary, args, null);
    }

    /// <summary>
    /// Seals a bundle whose nested code is already signed (--signDisableDeep), without re-signing it: rcodesign's
    /// --shallow signs the main executable and the bundle, and leaves nested binaries' existing signatures alone.
    /// </summary>
    public void SignBundleShallow(string bundlePath, string p12File, string p12PasswordFile, string appEntitlements)
    {
        var args = new List<string> {
            "sign",
            "--shallow",
            "--code-signature-flags", "runtime",
            "--p12-file", p12File,
            "--p12-password-file", p12PasswordFile,
            "--entitlements-xml-file", appEntitlements,
            bundlePath,
        };

        Log.Debug($"Sealing '{bundlePath}' with rcodesign (shallow)...");
        Exe.InvokeAndThrowIfNonZero(Binary, args, null);
    }

    /// <summary>
    /// Submits to Apple's notary service, waits for the verdict and staples the ticket to the bundle, in one call.
    /// <paramref name="apiKeyFile"/> is an App Store Connect API key in rcodesign's JSON form
    /// (`rcodesign encode-app-store-connect-api-key`).
    /// </summary>
    public void NotarizeAndStaple(string path, string apiKeyFile)
    {
        Log.Info("Preparing to Notarize with rcodesign. This will upload to Apple and usually takes minutes, " +
                 "[underline]but could take hours.[/]");

        var args = new List<string> {
            "notary-submit",
            "--api-key-file", apiKeyFile,
            "--staple",
            path,
        };

        var result = Exe.InvokeProcess(Binary, args, null);
        if (result.ExitCode != 0) {
            throw new UserInfoException(
                $"Notarization failed. 'rcodesign notary-submit' exited with code {result.ExitCode}:{Environment.NewLine}" +
                result.StdOutput + Environment.NewLine + result.StdErr);
        }

        Log.Debug(result.StdOutput);
        Log.Info("Notarization completed and stapled successfully");
    }
}
