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

## Repository Structure

```
src/
├── lib-csharp/          # Core C# library (Velopack NuGet package)
│   ├── Sources/         # Update sources (Velopack Flow, GitHub, GitLab, Gitea, HTTP, file)
│   ├── Locators/        # Platform-specific app locators (Windows, Linux, OSX)
│   ├── NuGet/           # Package handling (ZipPackage, PackageManifest)
│   └── UpdateManager.cs, VelopackApp.cs  # Primary public API
├── lib-rust/            # Core Rust library
├── bins/                # Rust update binaries (update.exe, UpdateNix, UpdateMac)
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
├── TestApp/                     # Test application used by integration tests
├── fixtures/                    # Test fixture files
├── PathHelper.cs                # Shared test utilities (linked into all test projects)
└── GlobalUsings.cs              # Shared usings (linked into all test projects)
samples/                         # Example apps (C#, C++, Node.js, Python, Rust)
```

## Architecture

**Core libraries**: `lib-csharp` and `lib-rust` are the two core libraries and must be kept in sync. Other language bindings (Node.js, Python, C++) are built on top of `lib-rust`. The libraries invoke the update binaries (`src/bins/` — e.g. `update.exe`) to perform actual install/update operations.

**Backwards compatibility**: Changes to the CLI/binaries must be backwards compatible, because older library versions shipped with apps in the field may call newer executables.

### Delta updates

The legacy delta system (`-delta.nupkg` files with zstd `.zsdiff` patches, `UpdateInfo.DeltasToTarget`, `vpk delta`,
`vpk pack --delta`) has been removed from lib-csharp, lib-rust, the bindings and vpk: nothing generates or consumes legacy
deltas any more, and clients always download the full package. A new chunk-based delta system is being built to replace it.
What remains for compatibility:
- The update binaries (`src/bins`) still implement the `patch` command (and keep their `zstd` dependency), because older
  libraries in the field call newer update binaries.
- `Delta` assets in existing `releases.{channel}.json` / `RELEASES` feeds still parse (`VelopackAssetType.Delta`,
  `IsDeltaFile`, `ReleaseEntry.IsDelta`, Rust `is_delta`); clients ignore them, and vpk keeps them when it merges or
  rewrites a feed.

**Chunk-based ("delta v2") engine, Rust side** (`src/lib-rust/src/chunks/`, exported via `maybe_pub!(chunks)` so the
bins use it): `index.rs` parses/validates the content map (Section A, `current/sq.chunks`) and transport map (Section B,
the `package/velopack/chunks.idx` trailer); `inflate.rs` reconstructs one sync-flushed deflate chunk (`flate2`);
`blob.rs` is the verified `chunks.bin` store; `plan.rs` decides which chunks to fetch and coalesces ranges; `staging.rs`
owns the `packages/staged/<id>/` layout, `ready.json` and GC; `assemble.rs` builds the staged tree from local files +
blob. All formats, caps, names and retry numbers are pinned in `chunks/mod.rs` and must stay byte-identical with
lib-csharp. The engine never touches anything outside the staged dir. Tests build indexes with the test-only writer in
`src/lib-rust/tests/common/index_writer.rs` (shared with `src/bins/tests` via `#[path]`); run lib tests with
`cargo test -p velopack --features public-utils`. Bins commands: `assemble --staged <dir> --parentPid <pid>` (no lock,
dies with the parent, stdout protocol `VPK:PROGRESS|MISSING|ERROR` at line start, exit codes 0/1/2/3/4) and
`apply-staged --staged <dir>` (Windows: validate, elevate, lock + `root/.velopack_swap_lock`, copy-under-root when the
staged dir is outside root, rename `current` → `current.old-<secs>-<hex>` then the new tree into place, hooks,
registry, shortcuts, cleanup). `commands::recover_current_dir` (called from `start` before the locator) renames the
newest `current.old-*` back if `current` is missing.

### Update Flow

1. **`VelopackApp.Run()`** executes at app startup. It handles fast-exit lifecycle hooks invoked by the update binary (`--veloapp-install`, `--veloapp-updated`, `--veloapp-obsolete`, `--veloapp-uninstall` — each with a version arg). These hooks have strict time limits (15-30s) and the process is killed if exceeded. It also auto-applies pending updates if a newer local package exists, fires `OnFirstRun`/`OnRestarted` callbacks based on environment variables (`VELOPACK_FIRSTRUN`, `VELOPACK_RESTART`), and cleans up old packages.

2. **`UpdateManager.CheckForUpdatesAsync()`** queries an `IUpdateSource` for the remote release feed, compares against the installed version, and returns `UpdateInfo` with `TargetFullRelease` (the latest `Full` asset) and `IsDowngrade`. Legacy `Delta` assets in the feed are ignored.

3. **`UpdateManager.DownloadUpdatesAsync()`** acquires a lock (`PackagesDir/.velopack_lock`), downloads the full package and verifies its checksum (SHA256 preferred, SHA1 fallback). On Windows, it also extracts the new `Update.exe` from the downloaded package. Local paths for feed assets always go through `PathUtil.GetSafeFilename` (C#, via `IVelopackLocator.GetLocalPackagePath`) / `misc::get_safe_filename` (Rust, via `local_package_path`): only the last path segment of `FileName` is kept, so a feed may reference packages in sub-folders or at absolute URLs (#596) and can never write outside `PackagesDir`. The raw `FileName` is still what gets resolved against the source's base URL/directory for the download.

4. **`UpdateManager.ApplyUpdatesAndRestart()`** invokes the update binary's `apply` command with `--waitPid`, `--rootDir`, `--packageDir`, and optional `--package` and restart args, then exits the app. The update binary waits for the app to exit, extracts the package, calls the app's fast-exit hooks, and relaunches.

### Packaging Flow

The `vpk pack` command (`PackageBuilder<T>` in `Velopack.Packaging`) runs platform-specific command runners:

1. **Preprocessing**: Locates the main executable, detects CPU architecture from the binary, copies files to a staging directory. On Windows: embeds icon in Update.exe, removes ClickOnce manifests, creates execution stubs. On Linux: builds an AppDir with `AppRun` script, `.desktop` file, and icon hierarchy. On macOS: creates or validates `.app` bundle structure.

2. **Code signing** (signtool `--signParams` on Windows only; `--signTemplate` and Azure Trusted Signing of Windows packages on all OSes; `codesign` on macOS): Signs all PE/Mach-O binaries. Supports `signtool.exe` (`--signParams`), custom sign templates, Azure Trusted Signing, and `codesign` with optional notarization (macOS).
   - **Azure Trusted Signing** (`--azureTrustedSignFile`, all OSes) does not use signtool: `Velopack.Packaging.Windows/Signing/` is a managed Authenticode signer (PE digest/embed + `SignedCms`, RFC3161 timestamp from `timestamp.acs.microsoft.com`) whose RSA key calls `Azure.CodeSigning.Sdk`. MSI files (Windows only) are signed via the `AzureSign.Core` package. Already-signed files are skipped: on Windows only if the signature is trusted (`CodeSign.ShouldSign`), on Linux/macOS if the PE has any certificate table. One `AzureTrustedSigner` is reused for the whole pack so the credential and certificate chain are fetched once; if the profile certificate rotates mid-pack (service reports another cert, or the signature does not verify with the cached one) the chain is refetched once and the file retried, for PE and MSI alike. Unlisted `ExcludeCredentials` are force-enabled only on Windows (Dlib parity); elsewhere SDK defaults apply, keeping SharedTokenCache/InteractiveBrowser off. After signing an MSI only signature integrity + signer certificate are checked, not trust (Private Trust profiles chain to roots Windows does not trust).

3. **Content indexing** (Windows target only, `WindowsPackCommandRunner.BuildChunkIndex`, after signing): rewrites
   `sq.version` so it is byte-identical to the `.nuspec`, then `Velopack.Packaging/Chunking/ChunkIndexBuilder` reads every
   pack-dir file once (FastCDC-gear-v1 boundaries via `FastCdc`/`GearTable`, per-chunk + per-file SHA-256, CRC-32) and writes
   the content map `packDir/sq.chunks` (Section A of the delta-v2 index, parsed by `Velopack.Chunks.ChunkIndex` in lib-csharp).
   Pack-time rejections (`UserInfoException`): symlinks/junctions, any nested `*Squirrel.exe`, non-top-level
   `*_ExecutionStub.exe`, paths that differ only by case, and the index path rules. macOS/Linux packs are unchanged (no index).

4. **Package creation** (parallel): Builds multiple outputs simultaneously:
   - **Release package** (`.nupkg`): ZIP with `lib/app/` containing all app files + update binary + `sq.version` manifest, plus a `.nuspec` with metadata. This is the canonical package format.
     With a content index it is written by `Chunking/ChunkedZipWriter` instead of `EasyZip`: each indexed file is deflated
     chunk by chunk (fresh `DeflateStream` + sync flush per chunk, `03 00` final block; stored when that is not smaller), so
     every chunk is a known byte range, and the index trailer (A ‖ B transport map) is the stored, **last** entry
     `package/velopack/chunks.idx` (`idx` is added to `[Content_Types].xml`). Entry order is root metadata, then
     `lib/app/Squirrel.exe`, then the rest of `lib/**` in ordinal UTF-8 order, so old readers' first-suffix matches
     (`.nuspec`, `Squirrel.exe`) are unchanged; no data descriptors, Zip64 only when needed. `VelopackAsset.FromZipPackageNoChecksum`
     locates the trailer and the feed gets `ChunkIndexOffset/Length/SHA256`. The `LegacyAppCanMigrate*` tests are the
     acceptance criterion for old-reader compatibility of this writer.
   - **Portable package**: On Windows: ZIP with `Update.exe`, `current/` dir, execution stub, and `.portable` marker. On Linux: AppImage (squashfs appended to runtime binary). On macOS: ditto ZIP of `.app` bundle.
   - **Setup installer**: On Windows: `setup.exe` template with the `.nupkg` appended as a bundle (offset+length header + signature). Optional MSI via WiX 5 compilation from Handlebars templates. On macOS: `.pkg` via `pkgbuild`.
   - **No delta packages**: vpk no longer creates legacy `-delta.nupkg` files (no `--delta` option, no `vpk delta`
     commands, no vendored `zstd.exe`). Delta assets already in a `releases.{channel}.json`/`RELEASES` feed or a
     release dir are still parsed and kept when vpk rewrites a feed (upload/download merges), never generated.

5. **Post-processing**: Writes `releases.<channel>.json` (asset feed for update clients) and legacy `RELEASES` file.

`test/Velopack.Packaging.Tests` multi-targets `net8.0;net10.0` so the chunk writer runs on both zlib (net8) and zlib-ng
(net10); the net10.0 build compiles only `Chunking/**` tests, and `TestTfmsInParallel=false` runs the two test processes
one after the other (they would otherwise collide on TempUtil's `temp.N` names and the TestApp publish output). Only
`RepackIsByteIdenticalOnNet8` asserts byte-identical repacks. The packed golden fixtures
`test/fixtures/delta-v2/ChunkTestApp-*.nupkg` + `releases.win.json` are produced by
`dotnet test --project test/Velopack.Packaging.Tests -f net8.0 -- --filter-method "*RegenerateChunkTestAppFixtures*" -explicit only`
(xunit v3 skips `Explicit` tests without `-explicit on|only`) and pinned (Section A, contents, feed pointers) by `ChunkTestAppFixturesMatchGenerator`.

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

## Windows 7 support

Our x86/x64 final artifacts — the binaries (update/setup/stub/testapp) and velopack_wix.dll (loaded by our MSI) — must
keep working on Windows 7. They are built on stable for the regular `*-pc-windows-msvc` targets, whose std statically
imports five Win8+ functions (`WaitOnAddress`/`WakeByAddressSingle`/`WakeByAddressAll`, `ProcessPrng`,
`GetSystemTimePreciseAsFileTime`), and windows-core links `CoTaskMemAlloc`/`CoTaskMemFree`/`CoCreateFreeThreadedMarshaler`
from `combase.dll` (Windows 8+; Windows 7 has them in ole32). `src/win7-shims` (`velopack_win7_shims::link_win7_shims!()`, invoked in the root of
each of those crates) defines the `__imp_` symbols for them via `global_asm!`, so the imports vanish and calls go through
shims that use the real function when present and a Win7 fallback otherwise. Do not move back to the tier 3
`*-win7-windows-msvc` targets (nightly + `-Z build-std` only, and they break on nightly).
- **Never link the shims into libraries end users link or load themselves** (velopack_libc, velopack_nodeffi): only
  into our own final artifacts that users don't consume directly.
- A new such artifact invokes `link_win7_shims!()` in its own root: the symbols have to be in the final crate's own
  object (an rlib definition loses to the import library), and `global_asm!` keeps them out of a cdylib's export
  table, unlike `#[export_name]`.
- If a new Rust version makes std import another Win8+ function, add a shim for it in `src/win7-shims` (both arch
  variants of the macro; x86 symbols are stdcall-decorated, e.g. `__imp__WaitOnAddress@16`).
- Never statically import Windows 8+ APIs; resolve them at runtime with `libloading` (see `splash.rs`/`mitigate.rs`).
- A new missing import shows up as setup/update exiting with `0xC0000139` (STATUS_ENTRYPOINT_NOT_FOUND) on Windows 7.
  If it comes from our own code or crates (e.g. xdialog), fix the caller there; shim only what std/windows-core link.
- CI covers this with the `win7` job in `build-tests.yml`: it boots a Win7 SP1 qcow2 under QEMU/KVM and runs
  `CrossCompile.RunCrossAppWindows7` over SSH (`VELOPACK_WIN7_SSH=user:password@host:port`). The .NET 8 TestApp
  can't run on Win7, so it uses the x64 and x86 rust testapp packages (`-rust` / `-rust-x86` artifact ids; the
  x86 testapp ships as `testapp_x86.exe`, and x86 runs under WOW64 on the x64 VM). Commands run through a scheduled
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
- Layout: `{App}.app/Contents/MacOS/UpdateMac`, `{App}.app/Contents/MacOS/sq.version`
- Packages: `~/Library/Caches/velopack/{AppId}/packages/`
- Never portable

**TestVelopackLocator** (C# only): Mock locator for unit tests — all properties throw unless explicitly set in the constructor.

## Key Conventions

- **Do not `cd` to the repo root** — you are already there. Never prefix commands with `cd /c/Source/velopack &&`. You may `cd` into a subdirectory only if genuinely needed.
- **Test framework**: xunit v3. Test helpers in `test/PathHelper.cs` — use `PathHelper.GetFixturesDir()` for test fixture paths and `PathHelper.IsCI` to detect CI.
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
