using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Packaging.Exceptions;

namespace Velopack.Packaging.Unix;

/// <summary>
/// Signs and notarizes macOS bundles with rcodesign (apple-platform-rs), a portable implementation of Apple's code
/// signature format and notary API, so <c>vpk [osx] pack</c> can sign and notarize on Linux and Windows too. It is chosen
/// by --signP12File: it never touches the keychain, so the certificate and its password travel as files, which suits CI.
/// https://gregoryszorc.com/docs/apple-codesign/stable/
/// </summary>
public class RcodesignTools
{
    private const string BinaryName = "rcodesign";

    /// <summary>
    /// How long notary-submit waits for Apple's verdict. rcodesign's own default is 10 minutes, and Apple's queue
    /// regularly takes longer than that, which would fail the release after a successful upload.
    /// </summary>
    public const int NotaryMaxWaitSeconds = 4 * 60 * 60;

    /// <summary>
    /// The oldest rcodesign vpk accepts: the release CI tests with. Older ones may lack arguments vpk passes
    /// (e.g. --for-notarization), which would only fail after the bundle has been built.
    /// </summary>
    public static readonly Version MinimumVersion = new(0, 29, 0);

    private const string InstallHint =
        "Install it with 'cargo install --locked apple-codesign' or download a release from " +
        "https://github.com/indygreg/apple-platform-rs/releases, and make sure it is on the PATH.";

    // rcodesign logs to stderr without level prefixes. It reports two problems while still signing and exiting 0. A file
    // in a code directory (e.g. Contents/MacOS) that is not a Mach-O is left out of the signature:
    private static readonly Regex UnsealedFileRegex = new(@"non Mach-O file with a nested rule:\s*(.+?)\s*$", RegexOptions.Multiline);

    // And a bundle whose Info.plist CFBundleExecutable names no file is sealed without a main executable, so the
    // entitlements and hardened runtime meant for it are applied to nothing. Nested bundles (logged between "entering
    // nested bundle" and "leaving nested bundle") may legitimately have none, e.g. a resource-only .bundle.
    private const string NoMainExecutableLine = "bundle has no main executable to sign specially";
    private const string EnteringNestedBundlePrefix = "entering nested bundle ";
    private const string LeavingNestedBundlePrefix = "leaving nested bundle ";

    private static readonly Regex SubmissionIdRegex = new(@"created submission ID:\s*(\S+)");

    private static readonly Regex VersionRegex = new(@"^\s*\S+\s+v?(\d+\.\d+\.\d+)");

    // The warn-level stderr lines (rcodesign's default filter) it writes for every sign: echoing its settings, then one
    // or more lines per file it signs, and every few seconds while waiting on the notary. They are only shown in verbose
    // output; anything else it says (e.g. a signature that will fail Apple's checks, nested bundles copied rather than
    // signed, Apple's notary log) is shown by default.
    private static readonly string[] ProgressPrefixes = {
        "registering signing key", "using time-stamp protocol server", "automatically setting team ID from signing certificate",
        "adding code signature flag", "setting entitlements XML", "signing bundle at", "signing main executable",
        "signing Mach-O file", "setting binary identifier to", "parsing Mach-O", "writing Mach-O to",
        "creating cryptographic signature", EnteringNestedBundlePrefix, LeavingNestedBundlePrefix, "poll state after",
    };

    // The lines that start with the path being signed: "signing <path> in place" (every sign) and "signing <path> as a
    // Mach-O binary" (a standalone Mach-O, e.g. UpdateMac with --signDisableDeep).
    private static readonly string[] ProgressSigningSuffixes = { " in place", " as a Mach-O binary" };

    // The code directories of a bundle (relative to Contents) whose files rcodesign must sign as nested code. This is the
    // "nested" rule of the bundle's resource rules, which matches Apple's.
    private static readonly string[] NestedCodeDirectories = {
        "MacOS", "Frameworks", "SharedFrameworks", "PlugIns", "Plug-ins", "XPCServices", "Helpers",
        "Library/Automator", "Library/Spotlight", "Library/LoginItems",
    };

    // The extensions of the bundle types macOS nests in an app, e.g. in Contents/Frameworks or Contents/PlugIns.
    private static readonly string[] NestedBundleExtensions = {
        ".app", ".framework", ".appex", ".xpc", ".bundle", ".plugin", ".kext", ".systemextension",
    };

    private readonly string _p12File;
    private readonly string _p12PasswordFile;

    private ILogger Log { get; }

    /// <summary>The full path of the rcodesign executable in use.</summary>
    private string BinaryPath { get; }

    private RcodesignTools(ILogger logger, string binaryPath, string p12File, string p12PasswordFile)
    {
        Log = logger;
        BinaryPath = binaryPath;
        _p12File = p12File;
        _p12PasswordFile = p12PasswordFile;
    }

    /// <summary>
    /// Finds rcodesign on the PATH and checks that it runs and is at least <see cref="MinimumVersion"/>, so a missing
    /// or outdated install fails before any packaging work. Signing then uses <paramref name="p12File"/> and the
    /// password stored in <paramref name="p12PasswordFile"/>.
    /// </summary>
    /// <exception cref="UserInfoException">rcodesign is not installed, does not run, or is too old.</exception>
    public static RcodesignTools Create(ILogger logger, string p12File, string p12PasswordFile)
    {
        var binaryPath = FindOnPath(Environment.GetEnvironmentVariable("PATH"));
        if (binaryPath == null) {
            throw new UserInfoException($"Signing with --signP12File needs '{BinaryName}', which was not found on the PATH. {InstallHint}");
        }

        (int ExitCode, string StdOutput, string StdErr, string Command) result;
        try {
            result = Exe.InvokeProcess(binaryPath, new[] { "--version" }, null);
        } catch (Exception ex) {
            throw new UserInfoException($"Could not run '{binaryPath}': {ex.Message} {InstallHint}", ex);
        }

        if (result.ExitCode != 0) {
            throw new UserInfoException(
                $"'{binaryPath} --version' exited with code {result.ExitCode}. {InstallHint}{Environment.NewLine}{result.StdErr}");
        }

        var versionOutput = result.StdOutput.Trim();
        var version = ParseVersion(versionOutput);
        if (version == null) {
            logger.Debug($"Could not read the version of '{binaryPath}' from '{versionOutput}', assuming it is recent enough.");
        } else if (version < MinimumVersion) {
            throw new UserInfoException(
                $"'{binaryPath}' is rcodesign {version}, but vpk needs {MinimumVersion} or later. {InstallHint}");
        }

        logger.Debug($"Using {versionOutput} ({binaryPath})");
        return new RcodesignTools(logger, binaryPath, p12File, p12PasswordFile);
    }

    /// <summary>The version in the output of `rcodesign --version` (e.g. "rcodesign 0.29.0"), or null.</summary>
    public static Version ParseVersion(string versionOutput)
    {
        if (String.IsNullOrEmpty(versionOutput)) {
            return null;
        }

        var match = VersionRegex.Match(versionOutput);
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
    }

    /// <summary>
    /// Returns the full path of rcodesign in one of the absolute directories of <paramref name="pathVariable"/>, or null.
    /// The PATH is searched explicitly, rather than letting the OS resolve a bare name, because on Windows that also
    /// searches the working directory first, and the process receives the certificate and API key paths. As in a shell,
    /// a file that is not executable is passed over on Linux and macOS.
    /// </summary>
    public static string FindOnPath(string pathVariable)
    {
        if (String.IsNullOrEmpty(pathVariable)) {
            return null;
        }

        var fileName = VelopackRuntimeInfo.IsWindows ? BinaryName + ".exe" : BinaryName;
        foreach (var entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            var dir = entry.Trim().Trim('"');
            if (dir.Length == 0 || dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(dir)) {
                continue;
            }

            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate) && IsExecutable(candidate)) {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (VelopackRuntimeInfo.IsWindows) {
            return true;
        }

        const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(path) & anyExecute) != 0;
    }

    /// <summary>
    /// Signs <paramref name="path"/> (an .app bundle or a single Mach-O) with the hardened runtime and a secure timestamp
    /// (rcodesign timestamps with Apple's server by default), with <paramref name="entitlements"/> on its main
    /// executable. See <see cref="BuildSignArgs"/> for what <paramref name="shallow"/>, <paramref name="forNotarization"/>
    /// and <paramref name="nestedCode"/> do.
    /// </summary>
    /// <exception cref="UserInfoException">
    /// The bundle has files rcodesign cannot seal (see <see cref="FindUnsealableFiles"/> and <see cref="ThrowIfUnsealed"/>),
    /// or no main executable (see <see cref="ThrowIfNoMainExecutable"/>).
    /// </exception>
    public void Sign(string path, string entitlements, bool shallow, bool forNotarization,
        IEnumerable<(string BundleRelativePath, string Entitlements)> nestedCode = null)
    {
        if (Directory.Exists(path)) {
            ThrowIfUnsealable(path, FindUnsealableFiles(path));
        }

        var args = BuildSignArgs(path, _p12File, _p12PasswordFile, entitlements, shallow, forNotarization, nestedCode);

        Log.Debug($"Signing '{path}' with rcodesign{(shallow ? " (shallow)" : "")}...");
        var result = Exe.InvokeProcess(BinaryPath, args, null);
        ProcessFailedException.ThrowIfNonZero(result);
        LogOutput(result.StdOutput, result.StdErr, LogLevel.Debug);
        // The scan above should leave rcodesign nothing to complain about; its own report backs it up.
        ThrowIfUnsealed(path, result.StdErr);
        ThrowIfNoMainExecutable(path, result.StdErr);
    }

    /// <summary>
    /// Submits to Apple's notary service, waits for the verdict (up to <see cref="NotaryMaxWaitSeconds"/>) and staples
    /// the ticket to <paramref name="path"/>, in one call. <paramref name="apiKeyFile"/> is an App Store Connect API key
    /// in rcodesign's JSON form (`rcodesign encode-app-store-connect-api-key`).
    /// </summary>
    public void NotarizeAndStaple(string path, string apiKeyFile)
    {
        Log.Info("Preparing to Notarize with rcodesign. This will upload to Apple and usually takes minutes, " +
                 "[underline]but could take hours.[/]");

        var result = Exe.InvokeProcess(BinaryPath, BuildNotarizeArgs(path, apiKeyFile), null);

        // rcodesign checks Apple's verdict before stapling and fails unless the submission was Accepted, so exiting 0
        // means the bundle was notarized and stapled.
        if (result.ExitCode != 0) {
            var submissionId = FindSubmissionId(result.StdErr);
            var message = submissionId == null
                ? $"Notarization failed: 'rcodesign notary-submit' exited with code {result.ExitCode}."
                : $"Notarization of submission {submissionId} failed: 'rcodesign notary-submit' exited with code {result.ExitCode}. " +
                  $"'rcodesign notary-log --api-key-file <file> {submissionId}' shows Apple's log. Fix the problem it reports, " +
                  "or re-run the pack if Apple was still processing the submission: nothing was released.";
            throw new UserInfoException(message + Environment.NewLine + result.StdOutput + Environment.NewLine + result.StdErr);
        }

        // Apple's log for an accepted submission can still carry warnings, so it is shown, as notarytool's output is.
        LogOutput(result.StdOutput, result.StdErr, LogLevel.Information);
        Log.Info("Notarization completed and stapled successfully");
    }

    /// <summary>
    /// The arguments for `rcodesign sign`.
    ///
    /// <paramref name="entitlements"/> is scoped to "@main" (the main executable), never unscoped: rcodesign splits a
    /// value at its first ':', so an unscoped Windows path (C:\...) would be read as the scope "C", leaving the main
    /// executable without entitlements, and a .NET app then dies at its first JIT.
    ///
    /// <paramref name="shallow"/> only stops rcodesign descending into nested bundles; Mach-O files directly in the code
    /// directories are re-signed either way. <paramref name="forNotarization"/> adds --for-notarization, which hardens
    /// every Mach-O at any depth but insists on a Developer ID certificate, so it is only passed when notarizing.
    ///
    /// Nested code keeps its existing signature flags and gets only what is scoped to its own path, so each of
    /// <paramref name="nestedCode"/> (bundle-relative, written with '/') is given the hardened runtime, and its
    /// entitlements unless null. The paths must be outside nested bundles (see <see cref="FindLooseMachOFiles"/>), and are
    /// passed with the host's separator, which is how rcodesign matches scopes.
    /// </summary>
    public static List<string> BuildSignArgs(string path, string p12File, string p12PasswordFile, string entitlements,
        bool shallow, bool forNotarization, IEnumerable<(string BundleRelativePath, string Entitlements)> nestedCode = null)
    {
        var args = new List<string> { "sign" };

        if (shallow) {
            args.Add("--shallow");
        }

        if (forNotarization) {
            args.Add("--for-notarization");
        }

        args.Add("--code-signature-flags");
        args.Add("runtime");
        args.Add("--p12-file");
        args.Add(p12File);
        args.Add("--p12-password-file");
        args.Add(p12PasswordFile);
        args.Add("--entitlements-xml-file");
        args.Add("@main:" + entitlements);

        if (nestedCode != null) {
            foreach (var (relativePath, nestedEntitlements) in nestedCode) {
                // rcodesign compares scopes with the paths it walks, which use the host's separator.
                var scope = Path.Combine(relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
                args.Add("--code-signature-flags");
                args.Add(scope + ":runtime");
                if (nestedEntitlements != null) {
                    args.Add("--entitlements-xml-file");
                    args.Add(scope + ":" + nestedEntitlements);
                }
            }
        }

        args.Add(path);
        return args;
    }

    /// <summary>The arguments for `rcodesign notary-submit`, waiting up to <see cref="NotaryMaxWaitSeconds"/> and stapling.</summary>
    public static List<string> BuildNotarizeArgs(string path, string apiKeyFile)
    {
        return new List<string> {
            "notary-submit",
            "--api-key-file", apiKeyFile,
            "--max-wait-seconds", NotaryMaxWaitSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--staple",
            path,
        };
    }

    /// <summary>
    /// The bundle-relative paths (written with '/') of the Mach-O files in <paramref name="bundlePath"/>'s Contents that
    /// are not inside a nested bundle (a framework, helper app, plug-in...), which rcodesign signs as part of the bundle
    /// itself. Symlinks are left out: rcodesign copies them as links, without signing what they point to.
    /// </summary>
    public static IReadOnlyList<string> FindLooseMachOFiles(string bundlePath)
    {
        var contents = new DirectoryInfo(Path.Combine(bundlePath, "Contents"));
        var found = new List<string>();
        if (contents.Exists) {
            CollectLooseMachOFiles(contents, "Contents", found);
        }

        return found;
    }

    private static void CollectLooseMachOFiles(DirectoryInfo dir, string relativeDir, List<string> found)
    {
        foreach (var file in dir.EnumerateFiles().OrderBy(f => f.Name, StringComparer.Ordinal)) {
            if (file.LinkTarget == null && BinDetect.IsMachOImage(file.FullName)) {
                found.Add(relativeDir + "/" + file.Name);
            }
        }

        foreach (var subDir in dir.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.Ordinal)) {
            var isNestedBundle = NestedBundleExtensions.Any(ext => subDir.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            if (subDir.LinkTarget == null && !isNestedBundle) {
                CollectLooseMachOFiles(subDir, relativeDir + "/" + subDir.Name, found);
            }
        }
    }

    /// <summary>
    /// The bundle-relative paths (written with '/') of the files in <paramref name="bundlePath"/> that rcodesign cannot
    /// seal: regular files in a code directory (Contents/MacOS, Contents/Frameworks...) that are not Mach-O. rcodesign
    /// leaves them out of the signature and still exits 0, so they stay in the bundle unsealed and macOS reports the
    /// signed app as damaged, even once notarized. Apple's codesign would seal them as resources.
    ///
    /// This follows rcodesign's walk: symlinks are sealed as links, and a directory with a '.' in its name is taken
    /// for a nested bundle (or a .dSYM), which is signed on its own, so neither is looked into.
    /// </summary>
    public static IReadOnlyList<string> FindUnsealableFiles(string bundlePath)
    {
        var found = new List<string>();
        foreach (var codeDir in NestedCodeDirectories) {
            var dir = new DirectoryInfo(Path.Combine(bundlePath, "Contents", Path.Combine(codeDir.Split('/'))));
            if (dir.Exists && dir.LinkTarget == null) {
                CollectUnsealableFiles(dir, "Contents/" + codeDir, found);
            }
        }

        return found;
    }

    private static void CollectUnsealableFiles(DirectoryInfo dir, string relativeDir, List<string> found)
    {
        foreach (var file in dir.EnumerateFiles().OrderBy(f => f.Name, StringComparer.Ordinal)) {
            if (file.LinkTarget == null && file.Name != ".DS_Store" && !StartsWithMachOMagic(file)) {
                found.Add(relativeDir + "/" + file.Name);
            }
        }

        foreach (var subDir in dir.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.Ordinal)) {
            if (subDir.LinkTarget == null && !subDir.Name.Contains('.')) {
                CollectUnsealableFiles(subDir, relativeDir + "/" + subDir.Name, found);
            }
        }
    }

    // As rcodesign tells a Mach-O apart: by its first four bytes alone.
    private static bool StartsWithMachOMagic(FileInfo file)
    {
        Span<byte> header = stackalloc byte[4];
        using var stream = file.OpenRead();
        return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length && BinDetect.HasMachOMagic(header);
    }

    /// <summary>
    /// Throws if <paramref name="unsealable"/> (files found by <see cref="FindUnsealableFiles"/> in <paramref name="path"/>,
    /// or reported by rcodesign) is not empty.
    /// </summary>
    /// <exception cref="UserInfoException">There is any such file.</exception>
    public static void ThrowIfUnsealable(string path, IReadOnlyCollection<string> unsealable)
    {
        if (unsealable.Count > 0) {
            throw new UserInfoException(
                $"rcodesign cannot seal {unsealable.Count} non-Mach-O file(s) in a code directory of '{path}', so the signed " +
                "bundle would fail verification on macOS: " + String.Join(", ", unsealable) + ". " +
                "Publish the app as a single file (PublishSingleFile=true), pack a pre-built .app that keeps such files in " +
                "Contents/Resources, or sign on macOS with --signAppIdentity.");
        }
    }

    /// <summary>
    /// Throws if rcodesign's <paramref name="stdErr"/> says it left files out of the signature of <paramref name="path"/>
    /// (see <see cref="FindUnsealedFiles"/>). It is the backup of the scan <see cref="Sign"/> does beforehand.
    /// </summary>
    /// <exception cref="UserInfoException">Any file was left out.</exception>
    public static void ThrowIfUnsealed(string path, string stdErr)
    {
        ThrowIfUnsealable(path, FindUnsealedFiles(stdErr));
    }

    /// <summary>
    /// The files rcodesign reported leaving out of a signature (see <see cref="UnsealedFileRegex"/>), from its stderr.
    /// </summary>
    public static IReadOnlyList<string> FindUnsealedFiles(string stdErr)
    {
        if (String.IsNullOrEmpty(stdErr)) {
            return Array.Empty<string>();
        }

        return UnsealedFileRegex.Matches(stdErr).Select(m => m.Groups[1].Value).Distinct().ToArray();
    }

    /// <summary>
    /// Throws if rcodesign's <paramref name="stdErr"/> says the bundle at <paramref name="path"/> has no main executable
    /// (see <see cref="ReportsNoMainExecutable"/>). It still exits 0 then, but the app's entitlements and hardened runtime
    /// were applied to nothing, so the main executable keeps whatever signature flags it had, and a .NET app dies at its
    /// first JIT. Apple's codesign fails in the same situation.
    /// </summary>
    /// <exception cref="UserInfoException">The bundle has no main executable.</exception>
    public static void ThrowIfNoMainExecutable(string path, string stdErr)
    {
        if (ReportsNoMainExecutable(stdErr)) {
            throw new UserInfoException(
                $"rcodesign found no main executable in '{path}': CFBundleExecutable in Contents/Info.plist must be the " +
                "file name of the app's main executable in Contents/MacOS.");
        }
    }

    /// <summary>
    /// Whether rcodesign's <paramref name="stdErr"/> says the bundle it signed (not a nested one) has no main executable.
    /// </summary>
    public static bool ReportsNoMainExecutable(string stdErr)
    {
        if (String.IsNullOrEmpty(stdErr)) {
            return false;
        }

        // rcodesign signs each nested bundle in turn, between its "entering" and "leaving" lines, then the bundle itself.
        var inNestedBundle = false;
        foreach (var line in stdErr.Split('\n').Select(l => l.Trim())) {
            if (line.StartsWith(EnteringNestedBundlePrefix, StringComparison.Ordinal)) {
                inNestedBundle = true;
            } else if (line.StartsWith(LeavingNestedBundlePrefix, StringComparison.Ordinal)) {
                inNestedBundle = false;
            } else if (!inNestedBundle && line == NoMainExecutableLine) {
                return true;
            }
        }

        return false;
    }

    /// <summary>The notary submission id rcodesign logs after uploading, from its stderr, or null.</summary>
    public static string FindSubmissionId(string stdErr)
    {
        if (String.IsNullOrEmpty(stdErr)) {
            return null;
        }

        var match = SubmissionIdRegex.Match(stdErr);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Whether a line of rcodesign's stderr is routine progress (one line per file, or a notary poll), rather than
    /// something to show without --verbose.
    /// </summary>
    public static bool IsProgressLine(string line)
    {
        var trimmed = line.Trim();
        return ProgressPrefixes.Any(p => trimmed.StartsWith(p, StringComparison.Ordinal))
               || (trimmed.StartsWith("signing ", StringComparison.Ordinal)
                   && ProgressSigningSuffixes.Any(s => trimmed.EndsWith(s, StringComparison.Ordinal)));
    }

    private void LogOutput(string stdOutput, string stdErr, LogLevel stdOutputLevel)
    {
        if (!String.IsNullOrWhiteSpace(stdOutput)) {
            if (stdOutputLevel == LogLevel.Information) {
                Log.Info(stdOutput.TrimEnd());
            } else {
                Log.Debug(stdOutput.TrimEnd());
            }
        }

        if (String.IsNullOrWhiteSpace(stdErr)) {
            return;
        }

        // rcodesign writes all its logging to stderr, without level prefixes.
        var lines = stdErr.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToArray();
        var progress = lines.Where(IsProgressLine).ToArray();
        var notable = lines.Where(l => !IsProgressLine(l)).ToArray();

        if (progress.Length > 0) {
            Log.Debug(String.Join(Environment.NewLine, progress));
        }

        if (notable.Length > 0) {
            Log.Info(String.Join(Environment.NewLine, notable));
        }
    }
}
