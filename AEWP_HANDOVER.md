# Handover: macOS Elevation Rework (AEWP) — Testing Guide

**Branch:** `cs/mac-aewp-elevation` (based on `develop` @ `7b3d6157` "Use atomic rename on macos")
**Status:** Implemented and reviewed on Windows; **never executed on a real Mac**. The whole point
of this handover is to verify it end-to-end on macOS. This doc can be deleted from the branch once
testing is complete.

## What changed and why

This branch replaces the `osascript ... with administrator privileges` elevation hack in the macOS
updater (context: velopack/velopack#50, and it builds on the #966 atomic-swap fix already in develop).

Previously, when applying an update to a non-user-writable `.app` (e.g. pkg-installed to
`/Applications` as a non-admin user), the updater shelled out to `osascript` running raw `mv`
commands. Problems: generic "osascript wants to make changes" password dialog, shell-quoting
fragility, non-atomic swap in the elevated path, and root-owned temp dirs the user process could
not clean up.

New behavior (all in `src/bins/src/commands/apply_osx_impl.rs`):

1. **The updater re-runs itself as root** (mirroring the Windows self-elevation pattern in
   `apply_windows_impl.rs`) via a new hidden `swap` subcommand, so elevated updates get the exact
   same code path as normal ones: atomic `renamex_np(RENAME_SWAP)` bundle exchange, LaunchServices
   `touch`, restore-on-failure, and root-owned temp dir cleanup.
2. **Elevation is performed with Authorization Services** (`AuthorizationCreate` +
   `AuthorizationExecuteWithPrivileges`, "AEWP"). The system auth dialog now shows a custom prompt
   naming the app: "*AppTitle* wants to install an update." (The `prompt` env item =
   `kAuthorizationEnvironmentPrompt`; unlocalized, same tradeoff Sparkle makes.)
3. **AEWP is deprecated (since 10.7), so it is never linked.** It is resolved at runtime via
   `dlsym(RTLD_DEFAULT, ...)`. If Apple ever removes the symbol, already-shipped UpdateMac binaries
   still load fine (only `AuthorizationCreate`/`AuthorizationFree` — fully supported APIs — are
   linked), and the code falls back to...
4. **osascript, kept as a permanent fallback** — used when AEWP is unavailable or fails for any
   reason other than the user cancelling. The fallback now also runs the `swap` subcommand instead
   of raw `mv`s, so it gets atomicity + cleanup too.

The elevation ladder in `run_elevated_swap()`:

```
dlsym finds AEWP?
├─ yes → AuthorizationCreate (shows password dialog, custom prompt)
│        ├─ user cancels (errAuthorizationCanceled -60006) → bail, NO second prompt, old app restarts
│        ├─ success → AEWP re-runs UpdateMac `swap ...` as root
│        │            ├─ output pipe contains VELOPACK_ELEVATED_SWAP_SUCCESS → done
│        │            └─ otherwise → fall through to osascript
│        └─ other auth error → fall through to osascript
└─ no  → osascript `do shell script "'UpdateMac' swap ..." with administrator privileges`
```

Success detection: AEWP does not report the child's exit code, so the elevated child prints the
marker constant `ELEVATED_SWAP_SUCCESS_MARKER` to stdout (captured via the AEWP communications
pipe, which the parent reads to EOF — that read is also how it waits for the child to exit). The
osascript path uses osascript's exit code instead.

Child logging: the parent passes `--log /dev/null --silent` to the elevated child, because a root
process appending to the shared log file would leave it root-owned and break future user-level
logging. The child's stdout (info-level log lines) flows back through the pipe and is re-logged by
the parent with an `[elevated]` prefix.

## Files changed on this branch

| File | Change |
| --- | --- |
| `src/bins/src/commands/apply_osx_impl.rs` | `mod authorization` (minimal hand-rolled FFI: `AuthorizationCreate`/`AuthorizationFree` linked, AEWP via dlsym), `run_elevated_swap`, `run_swap_via_authorization`, `run_swap_via_osascript`, `swap_bundles` (elevated child entry point), marker constant. The osascript `mv` chain is gone. |
| `src/bins/src/update.rs` | Hidden macOS-only `swap` subcommand (`--old`, `--new`, plus global `--rootDir`) and its handler. |
| `src/bins/src/commands/mod.rs` | Exports `swap_bundles` on macOS. |

Note: an earlier iteration used the `security-framework` crate; it was removed because its wrapper
strong-links the AEWP symbol (load-time bricking risk if Apple removes it) and 3.x exceeds the
workspace MSRV (1.75). No Cargo.toml/Cargo.lock changes remain on this branch.

## What could NOT be verified on Windows (your job)

The code compiles for Windows targets (`cargo check -p velopack_bins`), but everything under
`#[cfg(target_os = "macos")]` has never been compiled or run. Specifically unverified:

- The whole crate **compiles and links on macOS** (`#[link(name = "Security", kind = "framework")]`
  is new to this crate).
- The hand-rolled FFI signatures/struct layouts (`AuthorizationItem`, `AuthorizationItemSet`)
  behave correctly at runtime.
- The dlsym lookup actually finds `AuthorizationExecuteWithPrivileges` (it should — Security.framework
  is loaded because AuthorizationCreate is linked from it).
- The auth dialog appears, shows the custom prompt text, and credentials work.
- The AEWP child's argv/pipe plumbing (argv is NULL-terminated and does NOT include argv[0]; the
  pipe fd is taken via `fileno` + `File::from_raw_fd`).
- **Whether AEWP works at all on macOS Tahoe 26.x** — no removal reports were found as of 26.3, but
  this was the user's stated concern and is untested.

## Test plan

Build: `cargo build --release` (or via vpk pack for a full app). Run Rust unit tests first:

```bash
cargo test -p velopack_bins    # includes replace_bundle tests against real renamex_np
```

Then end-to-end scenarios, in order of importance:

1. **Happy path, no elevation** (regression check): admin user, user-writable app. Update should
   apply with NO password prompt (the whole-bundle rename works because `/Applications` is
   admin-writable, or the .app is user-owned). Log should NOT contain "Running elevated process".
2. **AEWP path**: standard (non-admin) user, app pkg-installed to `/Applications` (root-owned).
   Trigger an update. Expect:
   - Velopack's own "install update?" consent dialog (`ask_user_to_elevate`, unchanged), then
   - the system password dialog reading "*AppTitle* wants to install an update.",
   - log lines: `Running elevated process:` (not "via osascript"), `[elevated] ...` child output,
     `Bundle applied successfully via elevated process.`
   - `ls -di /Applications/YourApp.app` unchanged before/after (inode preserved → no duplicate
     Dock recents, the #966 fix carried into the elevated path).
   - Temp dirs under `~/Library/Caches/velopack/<AppId>/packages/VelopackTemp/` cleaned up.
   - The shared log file at its usual location is still user-owned/writable afterwards.
3. **Cancel behavior**: same setup, dismiss the password dialog. Expect: update aborts with
   "The user declined the elevation request.", the OLD app version relaunches, and — important —
   NO second (osascript) prompt appears.
4. **osascript fallback**: temporarily force it, e.g. make `find_execute_with_privileges()` return
   `None` (or break the symbol name string), rebuild, repeat scenario 2. Expect the generic
   osascript admin dialog, log line `Running elevated process via osascript:`, and a successful
   atomic swap all the same.
5. **Wrong password / auth failure paths** if time permits (3 wrong attempts → auth fails → should
   fall back to osascript prompt; arguably double-prompting here is acceptable, but observe it).

Useful log locations: the updater log (default path via the locator) plus the parent's console
output; the elevated child's output appears inline with the `[elevated]` prefix.

## Known caveats / future work (out of scope for this branch)

- The custom prompt line is English-only (Sparkle does the same; the right-name caching in the
  policy DB makes localized prompts awkward).
- No Touch ID: `system.privilege.admin` with the standard rule is password-only.
- Security hardening idea (Sparkle does this): before swapping, the elevated child could verify the
  new bundle's code signature / Team ID matches the installed app, so a compromised user session
  can't feed root an arbitrary bundle. The osascript approach had the same gap — not a regression.
- The truly non-deprecated long-term path is an `SMAppService` daemon (macOS 13+) with XPC; see
  velopack/velopack#50 for the full option analysis. This branch is the pragmatic middle step.

## If something is broken on macOS

Compile errors will be in `apply_osx_impl.rs` (FFI) or `update.rs` (the cfg-gated `swap` pieces) —
everything else is untouched. If AEWP itself misbehaves on Tahoe, the safest degradation is to make
`find_execute_with_privileges()` return `None` unconditionally, which turns the ladder into
"osascript running the `swap` subcommand" — still strictly better than the old `mv` chain.
