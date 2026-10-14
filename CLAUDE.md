# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**Keep this CLAUDE.md file up to date.** When the user tells you something about the project (conventions, patterns, warnings), eagerly add it here. If you make changes that conflict with what's documented here, update this file to match. The user will use git to revert if they disagree.

## Project Overview

Velopack is an installation and auto-update framework for cross-platform desktop applications. It has a C#/.NET frontend (library + CLI tool called `vpk`) and a Rust backend (update binaries for Windows/macOS/Linux). The library supports C#, C++, Node.js, Python, and Rust clients.

## CRITICAL: Do Not `cd` to the Repo Root

**The working directory is ALREADY the repo root.** Never write `cd /c/Source/velopack &&` or any variation — you are already there. Just run commands directly (e.g. `cargo build`, `dotnet test test/Velopack.Tests`). You may `cd` into a subdirectory if a specific task genuinely requires it, but never `cd` to the repo root itself.

## Build Commands

```bash
# .NET (main solution)
dotnet build                          # Build all .NET projects (Debug)
dotnet build -c Release               # Build all .NET projects (Release)

# Rust
cargo build                           # Build all Rust workspace members (Debug)
cargo build --release                  # Build all Rust workspace members (Release)
cargo bw                              # Alias for `cargo build --features windows` — required on Windows
                                      # to also build stub.exe/setup.exe (needed by packaging tests)
```

## Test Commands

```bash
# Run all .NET tests
dotnet test

# Run a specific test project (must use --project)
dotnet test --project test/Velopack.Tests
dotnet test --project test/Velopack.CommandLine.Tests
# Velopack.Packaging.Tests runs in well under a minute since TestApp publishes are
# cached per process and collections parallelize; running the whole project is fine.
# Targeted subsets: tests use xunit v3 on Microsoft.Testing.Platform, so filters go
# after `--` and use --filter-class / --filter-method with * wildcards
# (NOT the old VSTest --filter "FullyQualifiedName~..." syntax):
dotnet test --project test/Velopack.Packaging.Tests -- --filter-class "*MsiTests"

# Run a single test by name
dotnet test --project test/Velopack.Tests -- --filter-method "*TestMethodName*"

# Rust tests
cargo test
```

**Run tests locally in Debug (the default), never `-c Release`.** In Debug, `HelperFile` and
`PathHelper.GetRustBuildOutputDir()` resolve unsuffixed rust binaries from `target/debug`, so a plain
`cargo build` (add `--features windows` on Windows) is all the setup needed. Release-mode tests
expect CI's arch-suffixed vendored binaries (`update_x64.exe` etc.) — do not fake those locally by
copying files into `target/release`.

`npm test` in `src/lib-nodejs` rebuilds `update.exe` without the `windows` feature: run `cargo build --features windows`
again before running packaging tests.

## Repository Structure

```
src/
├── lib-csharp/          # Core C# library (Velopack NuGet package)
│   ├── Sources/         # Update sources (Velopack Flow, GitHub, GitLab, Gitea, HTTP, file)
│   ├── Locators/        # Platform-specific app locators (Windows, Linux, OSX)
│   ├── NuGet/           # Package handling (ZipPackage, PackageManifest)
│   └── UpdateManager.cs, VelopackApp.cs  # Primary public API
├── lib-rust/            # Core Rust library
├── bins/                # Rust update binaries (update.exe, UpdateNix, UpdateMac), the macOS assochelper, testapp
├── lib-cpp/             # C++ bindings via cbindgen
├── lib-nodejs/          # Node.js FFI bindings (neon)
├── lib-python/          # Python bindings via PyO3
├── vpk/                 # CLI tool entry point
│   ├── Velopack.Core/           # Core CLI abstractions
│   ├── Velopack.Packaging/      # Base packaging logic
│   ├── Velopack.Packaging.Windows/  # MSI, setup.exe (Handlebars templates)
│   ├── Velopack.Packaging.Unix/     # AppImage, tarball
│   ├── Velopack.Deployment/     # Upload to GitHub, S3, Azure, etc.
│   ├── Velopack.Flow/           # Application flow/orchestration
│   └── Velopack.Vpk/           # CLI entry using System.CommandLine
├── code-generator/      # Cross-language type generator
└── wix-dll/             # WiX MSI integration
test/
├── Velopack.Tests/              # Core library unit tests
├── Velopack.Packaging.Tests/    # Packaging/CLI tests
├── Velopack.CommandLine.Tests/  # CLI command parsing tests
├── Velopack.Deployment.Tests/   # vpk destination + cross-language update-source tests (docker stack)
├── Velopack.Pack.Tests/         # End-to-end pack/install/update tests per OS (Windows, macOS, Linux)
├── Velopack.CrossCompile.Tests/ # Cross-OS install tests (incl. the Windows 7 QEMU job)
├── Velopack.TestCommon/         # Shared test utilities: PathHelper.cs, TestApp.cs (TestApp publish cache)
├── TestApp/                     # Test application used by integration tests
├── LegacyTestApp/               # Squirrel/Clowd.Squirrel build scripts for the legacy-migration fixtures
├── fixtures/                    # Test fixture files
└── GlobalUsings.cs              # Shared usings (linked into all test projects)
samples/                         # Example apps (C#, C++, Node.js, Python, Rust)
```

## Architecture

**Core libraries**: `lib-csharp` and `lib-rust` are the two core libraries and must be kept in sync. Other language bindings (Node.js, Python, C++) are built on top of `lib-rust`. The libraries invoke the update binaries (`src/bins/` — e.g. `update.exe`) to perform actual install/update operations.

**Backwards compatibility**: Changes to the CLI/binaries must be backwards compatible, because older library versions shipped with apps in the field may call newer executables.

### Update Flow

1. **`VelopackApp.Run()`** executes at app startup. It handles fast-exit lifecycle hooks invoked by the update binary (`--veloapp-install`, `--veloapp-updated`, `--veloapp-obsolete`, `--veloapp-uninstall` — each with a version arg). These hooks have strict time limits (15-30s) and the process is killed if exceeded. It also auto-applies pending updates if a newer local package exists, fires `OnFirstRun`/`OnRestarted` callbacks based on environment variables (`VELOPACK_FIRSTRUN`, `VELOPACK_RESTART`), and cleans up old packages.

2. **`UpdateManager.CheckForUpdatesAsync()`** queries an `IUpdateSource` for the remote release feed, compares against the installed version, and builds a delta strategy. It selects deltas if: they exist, there are ≤10 in the chain, and their total size < the full package size. Returns `UpdateInfo` with `TargetFullRelease` and `DeltasToTarget[]`.

3. **`UpdateManager.DownloadUpdatesAsync()`** acquires a lock (`PackagesDir/.velopack_lock`), downloads deltas (or falls back to full package), verifies checksums (SHA256 preferred, SHA1 fallback), then invokes the update binary's `patch` command to reconstruct the full package from deltas. On Windows, it also extracts the new `Update.exe` from the downloaded package. Local paths for feed assets always go through `PathUtil.GetSafeFilename` (C#, via `IVelopackLocator.GetLocalPackagePath`) / `misc::get_safe_filename` (Rust, via `local_package_path`): only the last path segment of `FileName` is kept, so a feed may reference packages in sub-folders or at absolute URLs (#596) and can never write outside `PackagesDir`. The raw `FileName` is still what gets resolved against the source's base URL/directory for the download.

4. **`UpdateManager.ApplyUpdatesAndRestart()`** invokes the update binary's `apply` command with `--waitPid`, `--rootDir`, `--packageDir`, and optional `--package` and restart args, then exits the app. The update binary waits for the app to exit, extracts the package, calls the app's fast-exit hooks, and relaunches.

### Packaging Flow

The `vpk pack` command (`PackageBuilder<T>` in `Velopack.Packaging`) runs platform-specific command runners:

1. **Preprocessing**: Locates the main executable, detects CPU architecture from the binary, copies files to a staging directory. On Windows: embeds icon in Update.exe, removes ClickOnce manifests, creates execution stubs. On Linux: builds an AppDir with `AppRun` script, `.desktop` file, and icon hierarchy. On macOS: creates or validates `.app` bundle structure and, when associations are declared, builds the nested association helper bundle under `Contents/Helpers/` (see "File & URL associations").

2. **Code signing** (signtool `--signParams` on Windows only; `--signTemplate` and Azure Trusted Signing of Windows packages on all OSes; `codesign` on macOS): Signs all PE/Mach-O binaries. Supports `signtool.exe` (`--signParams`), custom sign templates, Azure Trusted Signing, and `codesign` with optional notarization (macOS). On macOS the association helper bundle is always signed with `HelperFile.VelopackEntitlements` and the outer bundle is sealed last (details in "File & URL associations").
   - **Azure Trusted Signing** (`--azureTrustedSignFile`, all OSes) does not use signtool: `Velopack.Packaging.Windows/Signing/` is a managed Authenticode signer (PE digest/embed + `SignedCms`, RFC3161 timestamp from `timestamp.acs.microsoft.com`) whose RSA key calls `Azure.CodeSigning.Sdk`. MSI files (Windows only) are signed via the `AzureSign.Core` package. Already-signed files are skipped: on Windows only if the signature is trusted (`CodeSign.ShouldSign`), on Linux/macOS if the PE has any certificate table. One `AzureTrustedSigner` is reused for the whole pack so the credential and certificate chain are fetched once; if the profile certificate rotates mid-pack (service reports another cert, or the signature does not verify with the cached one) the chain is refetched once and the file retried, for PE and MSI alike. Unlisted `ExcludeCredentials` are force-enabled only on Windows (Dlib parity); elsewhere SDK defaults apply, keeping SharedTokenCache/InteractiveBrowser off. After signing an MSI only signature integrity + signer certificate are checked, not trust (Private Trust profiles chain to roots Windows does not trust).

3. **Package creation** (parallel): Builds multiple outputs simultaneously:
   - **Release package** (`.nupkg`): ZIP with `lib/app/` containing all app files + update binary + `sq.version` manifest, plus a `.nuspec` with metadata. This is the canonical package format.
   - **Portable package**: On Windows: ZIP with `Update.exe`, `current/` dir, execution stub, and `.portable` marker. On Linux: AppImage (squashfs appended to runtime binary). On macOS: ditto ZIP of `.app` bundle.
   - **Setup installer**: On Windows: `setup.exe` template with the `.nupkg` appended as a bundle (offset+length header + signature). Optional MSI via WiX 5 compilation from Handlebars templates. On macOS: `.pkg` via `pkgbuild`.
   - **Delta package** (`.delta.nupkg`): Created if a previous release exists. Compares files between old and new releases — unchanged files get zero-length `.diff` markers, changed files get zstd patches (`.zsdiff`), new files included as-is. zstd is the only supported patch format (creation and apply); zstd being unavailable fails delta creation. Legacy `.bsdiff`/msdelta patches are rejected with an "Unsupported patch format" error on apply.

4. **Post-processing**: Writes `releases.<channel>.json` (asset feed for update clients) and legacy `RELEASES` file.

**`--signTemplate`** (`CodeSign.Sign`): the template is a shell command, run by `cmd.exe /C` on Windows and by
`bash -c` on Linux/macOS (passed via `ArgumentList`, unescaped, so quotes/`$VAR`/backticks behave as in bash).
On bash, `{{file}}` is replaced with `'\''`-escaped single-quoted paths, closing any `"`/`'` string the
placeholder sits in so each path stays one word. Do not reintroduce template escaping.

## Dialogs (src/l18n, xdialog 4)

All Setup/Update dialogs go through `velopack_l18n` (`dialogs.rs`, `progress.rs`) on xdialog 4. At startup the
binaries call `shared::init_dialog_icon()` (`src/bins/src/shared/app_icon.rs`), which finds the `vpk pack --icon`
image already in the package — no extra packaging needed: Windows reads icon group 1 that vpk embeds into
Setup.exe/Update.exe and rebuilds an `.ico`; Linux reads `{AppImage root}/.DirIcon`; macOS reads
`Contents/Resources/{CFBundleIconFile}`. Every dialog passes it as `icon_source` (window/taskbar icon).
Info, progress and question dialogs use `XDialogIcon::Custom` (via `general_icon`, which falls back to a stock
icon when no app icon was found); warning/error dialogs keep `Warning`/`Error`.

## File & URL associations

`vpk pack --fileAssociations ".myproj,.mytpl" --urlProtocols "myapp"` (Windows pack, macOS pack and bundle; removed from
Linux pack; MSBuild `VelopackFileAssociations` / `VelopackUrlProtocols`). Lists are normalized in
`Velopack.Packaging/AppAssociations.cs` (trim, lowercase, distinct, leading `.`, RFC 3986 schemes, both capped at 64
characters, reserved schemes like http/mailto/ms-* rejected); `PackageBuilder.AddAssociationMetadata` writes them to the
nuspec. Packaging does NOT decide what an activation does while the app is already running: that is the runtime
activation-forwarding opt-in (see VelopackApp below); no vpk option, MSBuild property, manifest element or plist key.
`vpk bundle` → `vpk pack --packDir X.app` (macOS): `vpk bundle` builds the helper bundle; `vpk pack` keeps an inherited
helper as-is (logged) unless given its own `--fileAssociations`/`--urlProtocols`, which rebuild it.

- **Windows**: the lists are written to the nuspec (`fileAssociations` / `urlProtocols`), read back through the
  `Manifest`/locator (`get_manifest_file_associations()` etc., which filter empty entries) and registered by Update.exe in
  `src/bins/src/windows/associations.rs` under `HKCU\Software\Classes` on install, synced on apply and on an
  overwrite/repair install (writes all of next, removes prev-minus-next; install passes the located previous install
  as prev) and removed on uninstall. Skipped for portable installs.
  Command target is `{root}\current\{mainExe}` with the flags `--veloapp-fileassoc "%1"` /
  `--veloapp-urlproto "%1"`. Every key we create (ProgId `{AppId}.{ext}`, protocol key) carries a
  `VelopackRootDir` string value; we only ever delete/modify keys whose marker equals our root dir, never touch
  `UserChoice`, and only take the `.ext` default when it is empty in both HKCU and HKLM (HKCR merges HKCU over HKLM,
  so a per-user default would shadow a machine-wide handler) or already ours. Association failures are logged
  as warnings and never fail install/apply/uninstall. Registry functions take the Classes base key (plus an optional
  read-only machine Classes key) so unit tests run against scratch `HKCU\Software\VelopackTests\<random>\Classes` keys.
  - **MSI installs** (`associations::Hive`): per-user MSIs register in HKCU, per-machine MSIs in HKLM (deployments run
    as SYSTEM via Intune/SCCM have no useful HKCU; when writing to HKLM only HKLM is consulted for the `.ext` default).
    `MsiTemplate.hbs` adds, only when the package declares associations (`HasAssociations`), `SyncAssociations*` /
    `RemoveAssociations*` deferred custom actions in a `Machine` (Impersonate=no, `ALLUSERS=1`) and a `User`
    (Impersonate=yes) variant, both calling `velopack_wix`'s `SyncAssociationsDeferred` / `RemoveAssociationsDeferred`
    with `[INSTALLFOLDER]"[ALLUSERS]` (trailing `\` trimmed so the marker equals Update.exe's root dir). Sync runs
    after `PatchChannelDeferred` on install/repair/upgrade; remove runs before `RemoveFiles` on uninstall *and* when the
    old product is removed by a major upgrade (`RemoveExistingProducts` is `afterInstallValidate`, so that finishes
    before the new product's sync; dropped associations are cleaned without the new MSI knowing the old manifest).
    `RollbackAssociations{Machine,User}` (`Execute="rollback"`, `RemoveAssociationsDeferred`, same conditions and
    impersonation as the sync) are scheduled right before each sync: the rollback script runs in reverse, so a failed
    install removes the registrations before `InstallFiles` is rolled back, while `current\sq.version` is still there.
    Every `velopack_wix` custom action runs under `with_msi_logging` (`src/wix-dll/src/msi.rs`): a `log::Log` impl
    forwards this thread's `warn!`/`info!` records (incl. the bins crate's association warnings) and
    `show_debug_message` traces to the MSI log via `MsiProcessMessage(INSTALLMESSAGE_INFO)`; visible with `/l*v`.
    Update.exe `apply` syncs into the hive the install's `MSI:{AppId}` ARP entry lives in
    (`registry::get_msi_install_scope`, HKMU-written so HKCU = per-user, HKLM = per-machine; `InstallLocation` breaks
    ties when both exist); HKLM is writable there because a Program Files apply already re-launches elevated, otherwise
    it warns. Update.exe `--uninstall` still bails for MSI installs (msiexec owns it).
- **VelopackApp** (`OnFileAssoc` / `OnUrlProto`, Rust `on_file_assoc` / `on_url_proto`, Node `onFileAssoc` /
  `onUrlProto`, Python `on_file_assoc` / `on_url_proto`, C `vpkc_app_set_hook_file_assoc` / `..._url_proto`): not
  fast-exit hooks. Only recognised when args[0] is one of the flags and a callback is registered for at least one of
  the carried flags; otherwise args are left to the app. **argv contract**: repeated leading `[flag, value]` pairs
  (parsing stops at the first non-flag; a flag with no/empty value is dropped with a warning) — this is how a restart
  forwards them. Exception: on Windows, when not restarted (no `VELOPACK_RESTART`), the OS shell launched us and passed
  exactly one value. It is recovered from the raw command line (`GetCommandLineW`: first token (program path) skipped,
  text after the flag, trimmed, one pair of quotes stripped) so quotes/backslashes survive — also when `SetArgs`/
  `set_args` was used, since apps commonly pass their own argv through. If the raw line does not contain the flag
  token (synthetic `SetArgs`), at most ONE pair is read from argv. Multi-pair parsing applies only to restarted
  launches and to non-Windows argv (the macOS helper passes several items as repeated pairs). A restart to auto-apply
  forwards the pairs instead of the original args; Update.exe re-quotes each argument, so argv is exact in the
  restarted process (UpdateMac restarts with `open -n <app> --args ...`, which also inherits `VELOPACK_RESTART`). The
  callbacks fire after the first-run/restarted hooks (once per pair) and startup continues, also when the app is not
  installed (dev build: the locator/manager cannot be created, the installed-startup part is skipped) and when the
  auto-apply fails (logged; both libraries then deliver the pairs in the running process). Values are never written
  to the log (only their length). Rust reads argv lossily (`args_os`), never panicking on non-Unicode file names.
  **Activation forwarding** — runtime opt-in, default OFF: C# `ForwardActivationsToRunningInstance(bool enabled = true)`,
  Rust `forward_activations_to_running_instance(enabled)`, Node `forwardActivationsToRunningInstance(enabled = true)`,
  Python `forward_activations_to_running_instance(enabled=True)`, C `vpkc_app_set_forward_activations_to_running_instance`,
  C++ `ForwardActivationsToRunningInstance(bEnabled = true)` (Node/Python/C/C++ just pass the flag to the Rust builder).
  Code: `src/lib-rust/src/activation_forwarding.rs`, `src/lib-csharp/Ipc/ActivationForwarding.cs`. Active only when
  enabled, an association callback is registered and the app is installed (any OS). `elect()` derives the app's IPC
  endpoint and tries the endpoint lock ONCE. Acquired → this process is the server. Held with no association args →
  plain extra instance, continues normally (never forwarded). Held with association args → send one `association`
  message with the pairs this build has a callback for (canonical flags) and wait for the `ack`, retrying
  connect/lock every 100 ms for up to 5 s (`CONNECT_WINDOW`); `ack` → log the count and `exit(0)` BEFORE the
  auto-apply (for Node/Python that exit happens in native code, like the fast-exit hooks). No server answered, an
  `error` reply, any send/verification failure or a message over the 1 MiB cap → `Standalone`: handled in-process as
  if forwarding were off (at-least-once: a slow callback can be handled twice, never lost; a request is never retried
  after it was sent). C# additionally keeps values with unpaired UTF-16 surrogates (NTFS names recovered from the raw
  command line) in-process, since JSON cannot carry them. On Windows the client calls
  `AllowSetForegroundWindow(server pid)` after verifying the pipe and before sending, so the callback may bring its
  window to the foreground; on macOS the helper activates the running app with `open`. The server starts inside the
  installed-startup path right after the auto-apply decision, BEFORE package cleanup and the
  `OnFirstRun`/`OnRestarted` hooks (the lock is held since the election; a late listener would make every activation
  spin for the whole window and duplicate). Received activations run the callbacks **on a Velopack IPC background
  thread**, serialized under one mutex/gate, possibly before `OnFirstRun`/`OnRestarted` or while the main thread is
  still inside `Run()`; the `ack` is sent only after the callback returned for every item, so callbacks must return
  promptly (post to the UI thread, do not block on it). Invalid items (unknown flag, empty value, no callback for that
  flag) → `error("unhandled association")` and nothing is invoked; a C# exception / Rust panic in a callback is logged
  and still acked. If the server cannot start, later activations simply start new instances (warning logged).
  Bindings: Node queues deliveries made while `run()` executes (own argv, or an IPC delivery racing startup) and calls
  the JS callback synchronously after `run()` returns; later IPC-thread deliveries go through a neon channel that is
  unref'd up front (`try_send`, so the server never pins the event loop; failure after loop shutdown is logged).
  Python callbacks on the IPC thread take the GIL via `Python::try_attach` and are skipped during interpreter
  shutdown. C: `p_user_data` must stay valid for the process lifetime.
  Tests: `test/Velopack.Pack.Tests/WindowsAssociationTests.cs` covers registration + raw-command-line recovery,
  `UrlProtoLaunchSurvivesAutoApply` (TestApp auto-applies for any `--veloapp-*` args[0]) and
  `ActivationForwardingSendsActivationToRunningApp` (`csharp` + `rust` variants). Both test apps (`test/TestApp`,
  `src/bins/src/testapp.rs`) enable forwarding iff `forward_activations.txt` exists next to the main exe, written at
  pack time by `TestApp.PreparePublishDir(..., forwardActivations: true)` like `test_string.txt`; both have a `wait`
  command that writes `ready`/`assoc-pid` and polls for a `stop` sentinel. In-process: C# `VelopackAppTests`
  `RunForwardActivations*` / `RunDoesNotForwardActivationsByDefault`, Rust `activation_forwarding.rs` tests and
  `app.rs` `test_run_forwarding_*` (Windows only where a second election must see a held lock: POSIX record locks are
  per process; the Unix cross-process cases are in `IpcInteropTests`). `src/lib-nodejs/test/VelopackApp.test.ts`
  elects the vitest process as server and has the Rust testapp (`ipc-send`) forward a url, covering the channel
  delivery after `run()`.
- **macOS**: LaunchServices ignores argv, so the libraries contain no AppKit code. vpk ships a small Rust helper app
  (`src/bins/src/assochelper.rs`, cargo bin `assochelper`, lipo'd by `build-rust.yml` into the vendored
  `AssocHelperMac`, `HelperFile.GetAssocHelperPath()`) as a NESTED bundle `{Main}.app/Contents/Helpers/{Name}.app`
  (`Name` = the main display name with `/`/`:` → `_`; exe `Contents/MacOS/AssocHelper`; id `{mainBundleId}.velopack-assoc`
  with chars outside `[A-Za-z0-9.-]` → `-`; same display name + icon as the main app so it looks right in Finder "Open
  With"; `LSUIElement` = no Dock icon; `LSMinimumSystemVersion` 10.13, so associations require 10.13+). Built by
  `Velopack.Packaging.Unix/OsxAssociationHelper.cs` (`Build`/`Find`/`FindAll`, cross-platform and unit-tested on
  Windows in `PlistWriterTests`; `BuildForBundle` reads the identity from the bundle's final Info.plist with
  PlistBuddy and is what both `vpk pack` and `vpk bundle` call, so a user `--plist` names the helper). The helper's
  Info.plist owns `CFBundleURLTypes` / `CFBundleDocumentTypes`; the main Info.plist never declares association keys (a
  user `--plist` is left alone; if it does declare them vpk warns, because LS may then route activations to the main
  bundle where the app's own framework gets them and the Velopack callbacks never fire). Icon order for the helper:
  `--icon`, else `Contents/Resources/{CFBundleIconFile}[.icns]`, else `{CFBundleIconName}.icns`, else the default
  Velopack icon (with a warning when the real icon lives in `Assets.car`). The helper owns its own `NSApplication`
  (objc2 / objc2-app-kit, `define_class!` delegate `VelopackAssocHelperDelegate`; no NSWorkspace):
  `application:openURLs:` turns file URLs into `--veloapp-fileassoc <path>` and everything else into
  `--veloapp-urlproto <absoluteString>` and coalesces a burst for 250 ms (`BATCH_DELAY`). A flush first tries to
  forward the batch over IPC to a running instance (endpoint derived from the main bundle's manifest exactly as the
  library does; one `association` request via `ipc::request`): on `ack` it runs `/usr/bin/open <Main.app>` to
  activate the app, otherwise (no endpoint, nobody listening, `error`, timeout) `/usr/bin/open -n <Main.app> --args
  <pairs>` (the new instance's own election may still forward). It never sets `VELOPACK_RESTART` and never launches
  the app with zero items. It exits when a flush finished and nothing new arrived within the next 250 ms, or after
  5 s (`NO_ACTIVATION_TIMEOUT`) with nothing received. It logs only counts/kinds/lengths into the main app's
  `~/Library/Logs/velopack_{id}.log`.
  **Signing** (`OsxPackCommandRunner.CodeSign`): the helper is ALWAYS signed with `HelperFile.VelopackEntitlements`
  (hardened runtime + timestamp), never with the user's `--signEntitlements` (sandboxed it would derive its socket path
  from a container-redirected temp dir and miss the running app, and its `open -n --args` launch could lose the
  arguments). Default `--deep` path: deep-sign the outer bundle, re-sign each helper (non-deep), then reseal the outer
  bundle once more with `deep: false` so the outer seal covers the helper. `--signDisableDeep`: UpdateMac → helpers →
  outer bundle, inside-out. Notarization, staple and spctl cover the nested bundle. `vpk bundle` never signs.
  **LaunchServices registration**: LS NEVER scans a nested `Contents/Helpers/*.app` on its own (verified on macOS 26
  with `lsregister -dump`), so the helper's schemes / document types are bindable only after an explicit
  `LSRegisterURL` on it (`src/lib-rust/src/launch_services.rs` `register_association_helpers`: outer bundle first, then
  each `Contents/Helpers/*.app` with an `AssocHelper`; plain CoreFoundation/CoreServices FFI; failures are warnings).
  Callers: the Rust library in-process at every installed start (`VelopackApp::run`); UpdateMac `apply` right after
  the post-swap `touch` of the bundle; UpdateMac's hidden `register-helpers` subcommand (logs to the file only, no
  console), which the C# library (no LS FFI of its own) starts detached from `VelopackApp.Run` when a helper exists and
  `{PackagesDir}/.velopack-helpers-registered` does not contain `"{version}\n{root}"` (written after spawning). The
  `.pkg` postinstall runs `lsregister -f` on the bundle and each helper as the console user (a root registration
  would land in root's LS database), and the pkg component plist sets `BundleIsRelocatable` false for `:0` and every
  `:0:ChildBundles:{n}` via PlistBuddy so Installer cannot "relocate" the helper onto a stale copy elsewhere on disk.
  Tests: `OsxPackTests.UrlActivationViaHelperReachesApp` (forwarding enabled) launches the app ONCE through `open`
  (all a portable zip user does), waits for `UpdateMac register-helpers` to finish, then (a) opens a url with no
  instance running (helper → `open -n --args`) and (b) opens one while a `wait` instance runs (helper → IPC forward);
  `UrlActivationSurvivesUpdate` applies v2 through UpdateMac and opens the scheme with no lsregister at all. Both
  install under `~/Applications/velopack-tests`, NEVER under TMPDIR: LS flags a bundle in `/var/folders/.../T` as
  `in-temp-dir launch-disabled` and refuses to bind its schemes. On an `open` failure they log the LS records for the
  bundle (`ProbeLaunchServices`); locally that skips (no GUI session), in CI it fails. `ExtractPkgToAppBundle` counts
  only top-level `.app` dirs.
  **Limitations** (accepted, documented in the `OnFileAssoc` docs of every language): the odoc intent (double-click,
  "Open With", drag-drop) lands on the helper, so the main app only gets a path string and its first access to a file
  in a protected folder (Desktop, Documents, Downloads, removable volumes) triggers the TCC "Files and Folders" prompt
  or fails with EPERM if denied earlier — apps should add the `NS*FolderUsageDescription` strings. Because the main
  Info.plist declares no document types, dropping a file onto the main app's Dock tile / Finder icon is refused (only
  the helper accepts the type).
- **Linux**: not supported, nothing is registered and the callbacks never fire.

### Association IPC (`src/lib-rust/src/ipc/`, `src/lib-csharp/Ipc/`)

A small, generic, language-agnostic IPC layer (the groundwork for #303 Responsive Hooks: Update.exe will pick a random
key, `IpcEndpoint::from_key(key)` (`[0-9a-z-]{1,40}`), start a server with no lock and hand the key to the hook process
via an env var; new message types/optional fields and a stop handle are the only additions needed — do not bump the
version for compatible changes). **C# ↔ Rust interop is a hard requirement** (the macOS Rust helper talks to C# apps;
Rust Update.exe will talk to apps of every language): both derive the identical endpoint and speak the identical wire
format. Rust: `ipc/mod.rs` (+ `windows.rs`, `unix.rs`, exposed via `maybe_pub!(ipc)`) on the `interprocess` crate's
sync local sockets, JSON via serde_json. C#: `IpcEndpoint.cs`, `IpcMessage.cs` (`IpcProtocol` constants, `IpcItem`,
`IpcMessage`, `IpcFraming`), `IpcConnection.cs`, `IpcServer.cs`, `IpcClient.cs`, `IpcNative.cs` (P/Invoke only), all
`internal` in `Velopack.Ipc`, on `System.IO.Pipes` `NamedPipeServerStream`/`NamedPipeClientStream` on every OS and TFM
(Unix: the absolute socket path is the pipe name, which .NET binds as a Unix domain socket). C# JSON goes through
`CompiledJson` (`IpcJsonSourceGenerationContext` STJ source-gen on net8+, Newtonsoft camelCase elsewhere) — never
hand-written JSON. Golden key vectors live in both `ipc/mod.rs` tests and `test/Velopack.Tests/IpcEndpointTests.cs`;
`test/Velopack.Tests/IpcInteropTests.cs` runs each side against the other through the Rust testapp's `ipc-endpoint`,
`ipc-send`, `ipc-serve` and `ipc-elect` commands (`src/bins/src/testapp.rs`; `ipc-elect` runs the real election and is
the only automated coverage of the Unix lock election). Those tests skip locally when the testapp is not built and
fail in CI.

- **Endpoint** (`IpcEndpoint { key, address, lock_path }`, `for_app(appId, root)` / `ForApp`): key = first 12 bytes
  (24 hex chars) of SHA-256 over `"velopack-ipc-v1\0{appId}\0{normalizedRoot}\0{user}"`. Root: Windows resolves the
  final path first (Rust `canonicalize`, C# `GetFinalPathNameByHandle`; 8.3 names, junctions, `subst`, case) then
  `normalize_root_windows` (`/`→`\`, strip `\\?\` / `\\?\UNC\`, trim trailing `\`, ASCII-lowercase only); Unix
  `realpath` then trim trailing `/`, no case change. User: Windows = the token user's string SID (same elevated or
  not), Unix = decimal uid. Windows: pipe `\\.\pipe\velopack-{key}`, lock `{LocalAppData}\velopack\ipc\{key}.lock`
  (never under RootAppDir: MSI roots are read-only). Unix: `{base}/velopack-{uid}/{key}.sock|.lock`, socket path ≤ 103
  bytes. `{base}` is one rule in both languages (Rust `ipc::unix::socket_base_dir`, C# `IpcEndpoint.SocketBaseDir`;
  tests `test_socket_base_dir` / `SocketBaseDirIsTheSameRuleAsRust`): macOS always `confstr(_CS_DARWIN_USER_TEMP_DIR)`
  (falls back to `/tmp`; `$TMPDIR` is ignored, it may be unset over ssh/launchd), elsewhere `$TMPDIR` when set and
  non-empty, else `/tmp`; trailing `/` trimmed. The dir is created 0700 (`AlreadyExists` is fine); a symlink disables
  IPC, a loose mode is tightened. Rust also refuses a foreign owner (`MetadataExt::uid`) and chmods the socket 0600; C#
  relies on chmod failing for a foreign dir plus `PipeOptions.CurrentUserOnly` (the runtime checks the peer uid on both
  ends, so a socket planted by another user, even in a dir root chmod'ed, is refused). Lock and socket deliberately
  live in the same dir: macOS purges unused temp entries after ~3 days and both must go together; a server whose files
  were purged is simply superseded by the next launch. Never move only one of them.
- **Framing + envelope** (unchanged wire format): u32 little-endian length N (1 ≤ N ≤ 1 MiB) + N bytes of compact
  UTF-8 JSON; EOF mid-frame / N = 0 / N > cap close the connection without a reply. `{"version":1,"type":"association",
  "items":[{"flag":"--veloapp-urlproto","value":"..."}]}`, `{"version":1,"type":"ack"}`,
  `{"version":1,"type":"error","message":"..."}`; `id` is an opaque correlation id copied into the reply;
  `items`/`message`/`id` omitted when empty; unknown fields ignored. Receivers reject only `version > 1`
  (`unsupported version`); 0/missing version, missing type, malformed or non-UTF-8 JSON → `invalid message`; unknown
  types → `unknown message type` (never a crash). Fixed error strings never contain values. v1 is one request + one
  reply per connection, but the framing is symmetric and multi-frame.
- **Security** — boundary = the current user only; NO integrity-level logic: elevated and non-elevated instances of
  the same user interoperate both ways. Windows server pipe descriptor `O:{userSid}D:P(A;;GA;;;{userSid})`: owner set
  explicitly to the user SID (an elevated token would default to Administrators), protected DACL allowing only the
  user; `PIPE_REJECT_REMOTE_CLIENTS`; the first instance uses `FILE_FLAG_FIRST_PIPE_INSTANCE`, so a squatted name
  fails the start. Rust passes the SDDL to `interprocess` (which sets both flags). C# builds a managed `PipeSecurity`
  (`IpcServer.CreatePipeSecurity`) and creates each instance with one `CreateNamedPipeW` P/Invoke
  (`IpcNative.CreatePipeServer`, wrapped in a `NamedPipeServerStream`): no managed API sets
  `PIPE_REJECT_REMOTE_CLIENTS` on any TFM, and `FirstPipeInstance` is net8+ only. Client (Rust `os::verify_server`,
  C# `IpcClient.VerifyServer`), before writing a byte: pipe owner (from the handle's security info) must equal its own
  user SID, any failure in that check is a refusal ("IPC server could not be verified"), never "continue" or a
  retry; access denied is an error, not "nobody listening". Then `AllowSetForegroundWindow(GetNamedPipeServerProcessId)`,
  best effort. C# connects with `TokenImpersonationLevel.Identification`; never `PipeOptions.CurrentUserOnly` on
  Windows (its client check compares against the token's default owner, which breaks mixed elevation). Unix: the 0700
  per-user dir plus (C#) `CurrentUserOnly`; only the lock holder replaces a stale socket before binding. SIGPIPE: Rust
  sets `SO_NOSIGPIPE` on Apple sockets and blocks `SIGPIPE` on its own `velopack-ipc-request` / `velopack-ipc-conn`
  threads elsewhere (so a C/C++ host is not killed by a vanished peer). Tests: C# `IpcServerClientTests`
  (`ConnectRefusesSquattedPipe`, `ConnectRefusesPipeOwnedByAnotherPrincipal` (elevated only),
  `ServerPipeIsOwnedByTheUserAndRejectsRemoteClients`: an SMB open via `\\localhost\pipe\...` is refused), Rust
  `ipc/windows.rs` `test_connect_refuses_squatted_pipe`.
- **Timeouts / connections**: `EXCHANGE_TIMEOUT` 5 s (client send+receive), `CONNECT_WINDOW` 5 s, `RETRY_INTERVAL`
  100 ms, C# `ServerIoTimeout` 5 s per served connection (the Rust server has none). Rust `ipc::request` runs connect +
  exchange on a `velopack-ipc-request` helper thread and abandons it with `TimedOut` after the timeout (local sockets
  have no I/O timeouts on Windows; the thread stays blocked until the peer goes away). C# `IpcConnection` does
  `ReadAsync`/`WriteAsync` + `Task.Wait` on the calling thread and disposes the stream on timeout (cancels the I/O on
  every TFM); `IpcClient.TryConnect` uses `Connect(10)` — never 0, which is `NMPWAIT_USE_DEFAULT_WAIT` — and maps
  `TimeoutException` / `IOException` 121 (`ERROR_SEM_TIMEOUT`, .NET Framework) to "nobody listening". The server binds
  synchronously, then a `velopack-ipc` / `Velopack IPC` accept thread owns the lock for the process lifetime and serves
  each connection on its own thread (C#: thread pool); the NEXT server instance is created before the current one is
  handed out so the name never disappears. Close after the reply: flush (Rust: `interprocess` flushes on drop; C#
  `WaitForPipeDrain`) then close — NEVER `DisconnectNamedPipe`/`Disconnect()`, which discards the unread `ack` and
  turns one activation into two. There is no stop API yet (#303).
- **Lock primitive** (`lockfile.rs` / `Util/LockFile.cs`, shared with the packages lock, same kernel primitive in both
  languages so a C# and a Rust process exclude each other): Windows opens with full sharing (read/write/delete) and
  takes an exclusive byte-range lock on byte 0 (`LockFileEx` fail-immediately / `FileStream.Lock(0, 1)`); contention
  is ONLY `ERROR_LOCK_VIOLATION` (33), a sharing violation or anything else is a failure (a `FileShare.None` open made
  Defender/indexer opens look like contention). Unix: `open(O_RDWR|O_CREAT|O_CLOEXEC, 0666)` + `lockf(F_TLOCK)`
  (CLOEXEC so the lock never leaks into child processes); contention = `EAGAIN`/`EWOULDBLOCK`/`EACCES`. C# cannot
  pass `open(2)`'s variadic mode through P/Invoke (Apple arm64 puts variadic args on the stack), so it opens with
  `O_RDWR|O_CLOEXEC` and on `ENOENT` does `creat(path, 0666)` + reopen; `O_CLOEXEC` is per-OS (Linux `0x80000`, macOS
  `0x1000000`). Rust `try_acquire_lock_once` / `is_lock_contention`, C# `TryLockOnce()` → `Acquired|Held|Failed`.
  Per-key lock files accumulate under the ipc dir (one 0-byte file per endpoint, by design).

## Windows 7 support

The Windows x86/x64 binaries are built for the `*-win7-windows-msvc` targets and must keep loading on Windows 7:
- Never statically import Windows 8+ APIs; resolve them at runtime with `libloading` (see `splash.rs`/`mitigate.rs`).
- windows-core links COM functions from `combase.dll` (Windows 8+); it is delay-loaded (`src/bins/build.rs`) and the
  delay-load failure hook in `src/bins/src/windows/delay_load.rs` substitutes `ole32.dll`. A new missing import shows
  up as setup/update exiting with `0xC0000139` (STATUS_ENTRYPOINT_NOT_FOUND) on Windows 7 — fix the caller (for our
  own crates like xdialog, fix it there) rather than working around it here.
- CI covers this with the `win7` job in `build-tests.yml`: it boots a Win7 SP1 qcow2 under QEMU/KVM and runs
  `CrossCompile.RunCrossAppWindows7` over SSH (`VELOPACK_WIN7_SSH=user:password@host:port`). The .NET 8 TestApp
  can't run on Win7, so it uses the rust testapp packages (`-rust` artifact ids). Commands run through a scheduled
  task in the desktop session, since Win32-OpenSSH kills a session's child processes (e.g. Update.exe) on exit.

## Locators

Locators (`IVelopackLocator` in C#, `VelopackLocator` in Rust) resolve platform-specific paths and app metadata. Both implementations follow the same logic and must stay in sync. All locators read app identity (ID, version, channel) from a `sq.version` manifest file.

**Windows** (`WindowsVelopackLocator` / `locator.rs`):
- Discovers install by finding `Update.exe` in the parent directory of the running executable
- Layout: `{RootAppDir}/Update.exe`, `{RootAppDir}/current/sq.version`, `{RootAppDir}/current/<app files>`
- Packages: `{RootAppDir}/packages/` if writable, otherwise falls back to `{LocalAppData}/{AppId}/packages/` (copies Update.exe there too). This fallback handles MSI installs to read-only locations like Program Files.
- Portable mode: detected by presence of `.portable` file in RootAppDir
- Legacy fallback: if no manifest, tries parsing version from `app-{version}` directory name

**Linux** (`LinuxVelopackLocator` / `locator.rs`):
- Apps are distributed as AppImage files. When an AppImage runs, it mounts a filesystem containing the app. The locator detects this by finding `/usr/bin/` in the current exe path and extracting the mount root before it.
- Requires `$APPIMAGE` environment variable (set automatically by AppImage runtime) pointing to the .AppImage file on disk
- Layout inside the mounted AppImage: `{root}/usr/bin/UpdateNix`, `{root}/usr/bin/sq.version`, `{root}/usr/bin/<app files>`
- Packages: `/var/tmp/velopack/{AppId}/packages/` (persists across reboots)
- Never portable

**macOS** (`OsxVelopackLocator` / `locator.rs`):
- Discovers install by finding `.app/` in the current exe path, extracting the bundle root
- Layout: `{App}.app/Contents/MacOS/UpdateMac`, `{App}.app/Contents/MacOS/sq.version` (a symlink to `Contents/Resources/sq.version`), and when associations are declared `{App}.app/Contents/Helpers/{Name}.app` (the association helper bundle; the locator ignores it)
- Packages: `~/Library/Caches/velopack/{AppId}/packages/`
- Never portable

**TestVelopackLocator** (C# only): Mock locator for unit tests — all properties throw unless explicitly set in the constructor.

## Key Conventions

- **Do not `cd` to the repo root** — you are already there. Never prefix commands with `cd /c/Source/velopack &&`. You may `cd` into a subdirectory only if genuinely needed.
- **Test framework**: xunit v3. Test helpers in `test/Velopack.TestCommon/PathHelper.cs` (linked into the test projects) — use `PathHelper.GetFixturesDir()` for test fixture paths and `PathHelper.IsCI` to detect CI.
- **Versioning**: Nerdbank.GitVersioning (NBGV) — version derived from git height, not manually set.
- **Assembly signing**: All assemblies signed with `Velopack.snk`.
- **Build output**: Goes to `build/{Configuration}/{TargetFramework}/` (not default `bin/`).
- **Rust output**: `target/debug/` or `target/release/`.
- **Main branch**: `develop` (not `main`/`master`). Releases from `master`.
- **Nullable reference types**: Enabled across all C# projects.
- **C# lang version**: `latest`.
- **Max line length**: 150 characters (C# and Rust).
- **Indent**: 4 spaces for C#, 2 spaces for XML/csproj/props, spaces everywhere (no tabs).
- **Rust formatting**: Always run `rustfmt` on Rust files after finishing edits.

## Deployment Tests (test/Velopack.Deployment.Tests)

Covers every `vpk` upload/download destination (local, S3, Azure, Gitea x3 versions, GitHub) and
update-*source* tests run cross-language via small CLI harnesses in 5 languages (C# in-process,
Rust, C++, Node.js, Python — see `harnesses/`). Destinations/sources run against a local docker
stack (Gitea, GitLab, Azurite, S3Mock), except GitHub which is live and uses a 5-repo lock pool
(`caesay/velopack-test-{1..5}`).

- **Start the stack once**, then re-run tests freely (see `test/Velopack.Deployment.Tests/docker/README.md`):
  ```bash
  docker compose -f test/Velopack.Deployment.Tests/docker/docker-compose.yml up -d
  ```
  GitLab takes several minutes to become healthy on first boot; other services are up in seconds.
- Tests **self-skip (never fail)** when a service, toolchain, or token is unavailable.
- **Live GitHub tests are currently disabled** (`DeploymentTestEnv.GitHubTestsDisabled = true`): their lock-pool
  polling shares the CI token's API rate limit and starved concurrent runs. Flip the flag to re-enable.
- Live GitHub tests need the `VELOPACK_DEPLOYMENT_TEST_TOKEN` env var (on Windows a User-level
  variable works — tests also read `EnvironmentVariableTarget.User`).
- Language harnesses are built once per test session (cargo / npm / maturin venv / cmake); a
  missing toolchain skips just that language's rows.
- **Avoid running test projects in parallel processes** — the C# TestApp is published once per
  test process (cached in `Velopack.TestCommon.TestApp`; the test string is injected via a
  `test_string.txt` file at pack time, not compiled in), but that publish still writes referenced
  projects' outputs to the shared `build/{Configuration}/` dir, so concurrent processes can race.

## Supply-chain audit

- `.github/scripts/supply_chain_audit.py` audits advisories for Rust (`cargo deny`), npm (`src/lib-nodejs`) and Python (`src/lib-python`, pip-audit).
- PR/push builds (`build-tests.yml` → `supply-chain`) gate on `cargo deny check bans licenses sources`, but only fail on advisories the change **introduces** vs the base commit (`diff --base`); pre-existing ones are warnings.
- `supply-chain.yml` runs daily on `develop` (`report`) and keeps one open issue labelled `supply-chain` while advisories exist, closing it when clean. Fix those on `develop` directly.

## Python type stubs (lib-python)

`src/lib-python` ships a committed `velopack.pyi` (PEP 561 stubs, generated by `pyo3-stub-gen`) plus a `py.typed` marker; maturin bundles both into the wheel via the `[tool.maturin] include` list. Regenerate after any change to the lib-python public API:

```bash
cd src/lib-python
cargo run --no-default-features --features stub-gen --bin stub_gen   # writes velopack.pyi
```

- The DTO stubs/docstrings come from `///` comments on the Rust structs and are emitted by `src/code-generator/Templates/python_asset.hbs` — regenerate `types.rs` with the code-generator, do not hand-edit it.
- **Requires Python ≥ 3.10** for generation (pyo3-stub-gen references `Py_3_10` types). The shipped wheel is still `abi3-py37` — the generator just builds `--no-default-features` (abi3 OFF) against the real interpreter. If only an older `python3` is on PATH, point pyo3 at a newer one, e.g. `PYO3_PYTHON=$(uv python find 3.12)`.
- pyo3 features are split so this works: the default `extension` feature enables `pyo3/extension-module` + `abi3` + `abi3-py37`; `--no-default-features --features stub-gen` turns those off and pulls in the optional `pyo3-stub-gen`. CI (`build-python.yml` → `stub-check`, on Linux) fails if `velopack.pyi` is stale.
