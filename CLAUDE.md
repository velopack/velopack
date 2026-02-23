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
```

## Test Commands

```bash
# Run all .NET tests
dotnet test

# Run a specific test project
dotnet test test/Velopack.Tests
dotnet test test/Velopack.CommandLine.Tests
# Do NOT run all of Velopack.Packaging.Tests — it takes far too long.
# Instead, run targeted subsets with --filter:
dotnet test test/Velopack.Packaging.Tests --filter "FullyQualifiedName~MsiTests"
dotnet test test/Velopack.Packaging.Tests --filter "FullyQualifiedName~MySpecificTest"

# Run a single test by name
dotnet test test/Velopack.Tests --filter "FullyQualifiedName~TestMethodName"

# Rust tests
cargo test
```

## Repository Structure

```
src/
├── lib-csharp/          # Core C# library (Velopack NuGet package)
│   ├── Sources/         # Update sources (GitHub, GitLab, Gitea, S3, Azure, HTTP)
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
├── TestApp/                     # Test application used by integration tests
├── fixtures/                    # Test fixture files
├── PathHelper.cs                # Shared test utilities (linked into all test projects)
└── GlobalUsings.cs              # Shared usings (linked into all test projects)
samples/                         # Example apps (C#, C++, Node.js, Python, Rust)
```

## Architecture

**Core libraries**: `lib-csharp` and `lib-rust` are the two core libraries and must be kept in sync. Other language bindings (Node.js, Python, C++) are built on top of `lib-rust`. The libraries invoke the update binaries (`src/bins/` — e.g. `update.exe`) to perform actual install/update operations.

**Backwards compatibility**: Changes to the CLI/binaries must be backwards compatible, because older library versions shipped with apps in the field may call newer executables.

### Update Flow

1. **`VelopackApp.Run()`** executes at app startup. It handles fast-exit lifecycle hooks invoked by the update binary (`--veloapp-install`, `--veloapp-updated`, `--veloapp-obsolete`, `--veloapp-uninstall` — each with a version arg). These hooks have strict time limits (15-30s) and the process is killed if exceeded. It also auto-applies pending updates if a newer local package exists, fires `OnFirstRun`/`OnRestarted` callbacks based on environment variables (`VELOPACK_FIRSTRUN`, `VELOPACK_RESTART`), and cleans up old packages.

2. **`UpdateManager.CheckForUpdatesAsync()`** queries an `IUpdateSource` for the remote release feed, compares against the installed version, and builds a delta strategy. It selects deltas if: they exist, there are ≤10 in the chain, and their total size < the full package size. Returns `UpdateInfo` with `TargetFullRelease` and `DeltasToTarget[]`.

3. **`UpdateManager.DownloadUpdatesAsync()`** acquires a lock (`PackagesDir/.velopack_lock`), downloads deltas (or falls back to full package), verifies checksums (SHA256 preferred, SHA1 fallback), then invokes the update binary's `patch` command to reconstruct the full package from deltas. On Windows, it also extracts the new `Update.exe` from the downloaded package.

4. **`UpdateManager.ApplyUpdatesAndRestart()`** invokes the update binary's `apply` command with `--waitPid`, `--rootDir`, `--packageDir`, and optional `--package` and restart args, then exits the app. The update binary waits for the app to exit, extracts the package, calls the app's fast-exit hooks, and relaunches.

### Packaging Flow

The `vpk pack` command (`PackageBuilder<T>` in `Velopack.Packaging`) runs platform-specific command runners:

1. **Preprocessing**: Locates the main executable, detects CPU architecture from the binary, copies files to a staging directory. On Windows: embeds icon in Update.exe, removes ClickOnce manifests, creates execution stubs. On Linux: builds an AppDir with `AppRun` script, `.desktop` file, and icon hierarchy. On macOS: creates or validates `.app` bundle structure.

2. **Code signing** (Windows and macOS): Signs all PE/Mach-O binaries. Supports `signtool.exe`, custom sign templates, Azure Trusted Signing (Windows), and `codesign` with optional notarization (macOS).

3. **Package creation** (parallel): Builds multiple outputs simultaneously:
   - **Release package** (`.nupkg`): ZIP with `lib/app/` containing all app files + update binary + `sq.version` manifest, plus a `.nuspec` with metadata. This is the canonical package format.
   - **Portable package**: On Windows: ZIP with `Update.exe`, `current/` dir, execution stub, and `.portable` marker. On Linux: AppImage (squashfs appended to runtime binary). On macOS: ditto ZIP of `.app` bundle.
   - **Setup installer**: On Windows: `setup.exe` template with the `.nupkg` appended as a bundle (offset+length header + signature). Optional MSI via WiX 5 compilation from Handlebars templates. On macOS: `.pkg` via `pkgbuild`.
   - **Delta package** (`.delta.nupkg`): Created if a previous release exists. Compares files between old and new releases — unchanged files get dummy markers, changed files get zstd patches (`.zsdiff`, falls back to bsdiff), new files included as-is.

4. **Post-processing**: Writes `releases.<channel>.json` (asset feed for update clients) and legacy `RELEASES` file.

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
