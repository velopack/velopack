# Velopack Cross-Language SDK — Architecture Handover

**Status:** Proposed architecture, not yet started
**Audience:** Coding agent picking up implementation
**Last updated:** 2026-05-24

---

## 1. Purpose

This document describes the agreed approach for unifying Velopack's per-language
update SDKs onto a single canonical implementation, and provides a task breakdown
to begin work. Read it fully before writing code. Where it says "verify" or
"audit", do that first — several decisions depend on findings from those checks.

---

## 2. Background and problem

Velopack provides consuming applications with an update SDK exposing three
operations:

- `checkForUpdates` — reads local Velopack metadata, queries a remote source for
  available releases, returns what (if anything) applies. Runs fully in the
  consuming process.
- `downloadUpdates` — downloads the applicable release to the local filesystem.
  Runs fully in the consuming process.
- `applyUpdates` — launches the already-shipped `Update.exe` (or platform
  equivalent) to replace the running application and complete the update.

The SDK also exposes an `IUpdateSource` extension point so application authors can
implement updates from arbitrary backends (e.g. Google Drive, S3, a private feed).

Today the core logic is implemented more than once — a Rust core (which builds a
native C library with a C++ wrapper header) and a separate C# implementation.
Adding each new language (JS, Java, Dart, Go, ...) currently means either another
native-binding surface or another reimplementation. Both are unsustainable.

Two hard constraints shaped the chosen approach:

1. **The JS SDK must not ship native (`.node`) code.** Native Node addons cause
   ongoing pain for consumers using Vite/webpack and create a per-platform
   prebuild matrix. The JS package must be loadable in any bundler with no native
   dependency.
2. **One canonical source.** New languages must be added by writing a thin host
   shim, not by reimplementing the update engine.

---

## 3. Target architecture

```
                  ┌─────────────────────────┐
                  │   Rust core (canonical)  │
                  │   async, no native I/O   │
                  └────────────┬────────────┘
                               │ compiled to
                               ▼
                  ┌─────────────────────────┐
                  │  velopack.wasm           │
                  │  (WASM Component, WASI)  │
                  └────────────┬────────────┘
                               │ hosted by
        ┌──────────┬───────────┼───────────┬──────────┬──────────┐
        ▼          ▼           ▼           ▼          ▼          ▼
      JS/TS       C#         Java         Go        Python     Dart
      (jco)   (wasmtime-  (Chicory)    (wazero)  (wasmtime-    (TBD)
              dotnet)                              py)
```

The Rust core is compiled to a **WebAssembly Component** targeting WASI 0.2
(`wasm32-wasip2`). Each language SDK is a thin host that loads `velopack.wasm`,
satisfies its imports, and re-exposes its exports as an idiomatic API in that
language. The `.wasm` artifact is CPU-architecture-independent — one blob runs on
every platform; the host runtime JITs/interprets it for the local CPU.

`Update.exe` is unchanged and remains native. The WASM core does not replace it;
`applyUpdates` simply launches it.

---

## 4. Key decisions and rationale

These were settled during design discussion. Do not relitigate them without new
information.

**Canonical core is Rust, not C#.** Rust compiles to small, fast WASM with no
runtime/GC overhead — it is the most mature WASM toolchain and the reference
target for every new WASI feature. C# → WASM drags in the .NET runtime (multi-MB
components, slower cold start) and perpetually trails Rust on WASI feature
support. The existing Rust core is the better starting point.

**WASM Component Model, not raw core modules.** The Component Model gives a
language-agnostic interface boundary (WIT), which is what makes the
one-source-many-hosts story work. Bindgen tooling targets components.

**Distribution model.** The JS package ships `velopack.wasm` (one file, no
platform matrix) plus generated JS — base64-inline the wasm if needed so consumers
need zero bundler config. Other languages (C#, Python, Go, Java) embed the `.wasm`
as a resource. C#/Python hosts additionally carry the runtime's own native binary
per-platform; that is acceptable because it is one stable dependency, not
per-release native code.

**Async, not threads.** WASM threads do not yet compose with the Component Model
(the shared-everything-threads proposal is unfinished; host coverage across
Go/Java/JS is uneven). The check/download workload is I/O-bound, not CPU-bound, so
async is the correct model anyway and maps cleanly to every target language's
idioms (`Promise`, `CompletableFuture`, goroutines, `Future`). The current Rust
core is threaded only out of convenience and must be rewritten async. Any
genuinely CPU-bound parallel work stays in native `Update.exe`.

**HTTP via `wasi:http`, not reqwest.** reqwest has no production-grade
`wasm32-wasip2` support — the available forks are runtime-specific or depend on
non-portable TLS crypto. `wasi:http` is host-provided, so each language SDK uses
its own native HTTP stack and trust store. Use `wstd::http` as the ergonomic Rust
wrapper over `wasi:http`.

---

## 5. The component interface (WIT)

The interface is defined in WIT. The sketch below is a **starting point**, not
final — refine it as the port progresses.

```wit
package velopack:core@0.1.0;

interface types {
    record update-info {
        target-version: string,
        assets: list<release-asset>,
        // extend as needed
    }
    record release-asset {
        url: string,
        size: u64,
        sha256: string,
    }
    variant update-error {
        network(string),
        filesystem(string),
        invalid-metadata(string),
        cancelled,
    }
}

// CUSTOM IMPORT — host must implement. WASI has no process spawning.
interface host-process {
    launch-detached: func(path: string, args: list<string>)
        -> result<_, string>;
}

// CUSTOM IMPORT — pluggable update source (the IUpdateSource equivalent).
interface update-source {
    resource source {
        get-release-feed: func() -> result<string, string>;
        download-asset: func(url: string, dest-path: string)
            -> result<_, string>;
    }
}

// CUSTOM IMPORT — download progress callback to the host.
interface progress {
    report: func(bytes-done: u64, bytes-total: u64);
}

world velopack {
    // Standard WASI imports — satisfied by the host runtime for free.
    import wasi:filesystem/types@0.2.0;
    import wasi:http/outgoing-handler@0.2.0;
    import wasi:clocks/wall-clock@0.2.0;
    import wasi:random/random@0.2.0;

    // Custom imports — each host shim implements these.
    import host-process;
    import update-source;
    import progress;

    use types.{update-info, update-error};

    export check-for-updates: func() -> result<option<update-info>, update-error>;
    export download-updates: func(info: update-info) -> result<_, update-error>;
    export apply-updates: func(info: update-info, restart: bool)
        -> result<_, update-error>;
}
```

**`IUpdateSource` design note.** Support two paths: (a) built-in sources (HTTP
feed, etc.) implemented in Rust and compiled into the core; (b) custom sources
implemented in the host language via the `update-source` imported resource. Most
consumers will use a built-in source; the import exists for the extension case.

---

## 6. Standard vs custom imports — what you do and don't implement

**You do NOT implement** the standard WASI interfaces. When the Rust core uses
`std::fs`, `wstd::http`, `std::time`, etc., those become imports against
`wasi:filesystem`, `wasi:http`, `wasi:clocks`. Every mature host runtime (jco,
wazero, Chicory, wasmtime-dotnet, wasmtime-py) already implements these and
bridges them to the host OS. Zero shim code from you.

**You DO implement**, per host language, only the custom imports — realistically
just `host-process.launch-detached`, the `update-source` resource (when a host
provides a custom source), and `progress.report`. Each is small: process launch is
~10 lines (`child_process.spawn` / `ProcessBuilder` / `os/exec` /
`System.Diagnostics.Process` / `subprocess`).

Realistic host shim size per language: ~50–300 lines, dominated by (a) instantiate
the component with the runtime's WASI implementation wired up, (b) implement the
custom imports, (c) marshal exports into an idiomatic API class.

---

## 7. The async rewrite

The Rust core must move from threaded to async. Guidance:

- Use **`wstd`** (Bytecode Alliance) as the async stdlib for WASI components.
  async-std/tokio/smol do not yet support WASI 0.2 components; `wstd` is the
  transitional runtime and is intentionally small.
- HTTP: `wstd::http`. Filesystem: `wstd::io` over WASI filesystem streams. Timers:
  `wstd::task::sleep`.
- **Add `Send` bounds to trait definitions even though `wstd` does not require
  them.** WASI 0.2 is single-threaded so `wstd` omits `Send`, but adding the
  bounds now makes a future migration to tokio (when it gains component support) a
  mechanical change rather than a rewrite.
- Push side effects to the edges: every OS-touching operation should be an
  explicit import call, leaving the core deterministic given mocked imports. This
  is also a testability win.
- Build a single internal `velopack-http` module wrapping `wstd::http` with the
  helpers actually needed: GET with retry, ranged GET for resumable downloads,
  redirect-following (`wasi:http` does NOT follow 3xx automatically — implement
  the loop with a max-redirect counter), and streaming download-to-file with
  progress reporting. Feature-gate the implementation so a non-WASM native build
  can swap in reqwest behind the same API if ever needed.

---

## 8. Per-language host strategy

| Language | Runtime / host    | Bindgen                         | Package      | Maturity |
|----------|-------------------|---------------------------------|--------------|----------|
| Rust     | native build      | `wit-bindgen` / `cargo component`| crate        | High     |
| JS/TS    | `jco`             | `jco transpile`                 | npm          | High     |
| C#       | `wasmtime-dotnet` | component host (manual marshal) | NuGet        | Good     |
| Go       | `wazero`          | `wit-bindgen-go`                | Go module    | Good     |
| Java     | `Chicory`         | Chicory component tooling       | Maven        | Emerging |
| Python   | `wasmtime-py`     | `python -m wasmtime.bindgen`    | PyPI wheel   | Good     |
| Dart     | TBD               | TBD                             | pub          | **Unresolved** |

**Recommended order:** JS first (highest-quality host, validates the no-native-code
goal), then Go (`wazero` is pure-Go, zero CGo). These two validate the WIT design.
Then C# via `wasmtime-dotnet` — note its component-model support needs more manual
marshalling glue than jco; budget for that. Then Java. **Dart is genuinely
unresolved** — there is no mature pure-Dart component-capable WASM runtime. Treat
Dart as TBD and re-evaluate when it is actually needed; a hand-port against the
same WIT spec is the likely fallback.

For C# specifically: the resulting NuGet package keeps the *same public API* the
existing C# SDK exposes, so existing consumers see no change — they still
`dotnet add package Velopack`. Internally it hosts `wasmtime-dotnet` and loads the
component.

---

## 9. Constraints and known risks

- **Binary size.** A non-trivial Rust crate compiled to WASM is typically
  200KB–2MB after `wasm-opt -Oz`. Run `wasm-opt`; avoid `wee_alloc` (dead); strip
  panic/`std::fmt` paths where possible.
- **No threads.** Confirmed out of scope — see §4.
- **Windows-specific filesystem ops.** WASI's filesystem interface is
  POSIX-flavoured. If the check/download paths rely on NTFS junctions, alternate
  data streams, `MoveFileEx` semantics, `FILE_SHARE_DELETE`, long-path (`\\?\`)
  prefixes, or registry access, those need custom WIT imports. **This is the
  biggest unknown — audit before committing (see §12).**
- **TLS / certs.** Delegated to the host runtime's HTTP stack. This is desirable
  (native trust store per platform) but means cert behaviour differs subtly per
  language host — note in docs.
- **HTTP feature gaps.** `wasi:http` lacks automatic redirect following, cookies,
  and possibly transparent gzip/brotli decompression. Redirects: implement
  manually. Compression: verify per host; worst case send
  `Accept-Encoding: identity` and decode in Rust with `flate2`.
- **Dart.** No viable host today — see §8.

---

## 10. Migration plan

Do this incrementally, not as a big-bang rewrite.

1. Carve out one small, self-contained piece of Velopack logic (e.g. metadata
   parsing / version applicability) into the Rust core and compile to a component.
2. Host that component inside the **existing C# SDK** via `wasmtime-dotnet`,
   behind the existing C# implementation as a fallback. Verify the full pipeline
   end to end.
3. Expand the WASM core piece by piece, keeping the native implementation as
   fallback during transition.
4. Once the C# host validates the architecture, build the JS (jco) and Go
   (wazero) hosts against the now-stable WIT.
5. Add Java. Defer Dart.
6. When the WASM core is complete, the per-language "implementation" is just the
   host shim; retire the duplicated native cores.

---

## 11. Immediate next steps

In order:

1. **Audit (see §12) — do this before writing core code.**
2. Stand up a minimal Rust component: `cargo component new`, a trivial export, a
   trivial custom import. Build to `wasm32-wasip2`. Confirm it loads and runs
   under `jco` and under `wasmtime` CLI.
3. Draft the real `velopack.wit` from the §5 sketch, informed by the audit.
4. Implement the `host-process` custom import in a JS host shim and confirm
   `launch-detached` can start a dummy process.
5. Port the first logic slice (metadata parsing / applicability) to async Rust
   using `wstd`. Add `Send` bounds to traits.
6. Build the `velopack-http` wrapper module over `wstd::http` with retry, ranged
   GET, manual redirect handling, and streaming-to-file with progress.
7. Wire the slice into the existing C# SDK via `wasmtime-dotnet` as per migration
   step 2.

---

## 12. Open questions — audit before committing

Resolve these early; later decisions depend on them.

- **Windows API surface in check/download paths.** Grep the current Rust core for
  `windows::`, `winapi::`, and `#[cfg(windows)]` within the check and download
  code paths. Whatever appears is the candidate list for custom WIT imports. A
  handful → WASM is clearly correct. Pervasive → reconsider, or lift those ops
  into `Update.exe`. (`Update.exe`'s own exe-replacement code is out of scope —
  it stays native.)
- **jco WASI filesystem coverage.** Verify jco's WASI polyfill supports the
  specific filesystem operations the core needs on Windows, particularly anything
  involving file locking, atomic rename, or long paths. Gaps become custom
  imports.
- **`wasmtime-dotnet` + NativeAOT.** Confirm `wasmtime-dotnet` works under
  `PublishAot=true` for the minimum supported .NET TFM, since some consuming apps
  publish AOT.
- **Compression in `wasi:http`.** Confirm whether each target host's `wasi:http`
  implementation transparently handles gzip/brotli, or whether the core must
  decode.
- **Non-desktop C# usage.** If the existing C# SDK is used in iOS/MAUI/Unity
  contexts, the `wasmtime-dotnet` native dependency needs per-target validation
  (iOS dynamic-library rules in particular). Moot if C# usage is desktop-only —
  confirm.
