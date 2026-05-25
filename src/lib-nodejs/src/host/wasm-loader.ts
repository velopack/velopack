import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import * as hostProcess from "./host-process.js";
import * as hostFilesystem from "./host-filesystem.js";
import * as hostLogger from "./host-logger.js";
import * as progress from "./progress.js";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

let _instance: any = null;
let _initPromise: Promise<any> | null = null;

export async function loadVelopack(): Promise<void> {
  if (_instance) return;
  if (_initPromise) {
    await _initPromise;
    return;
  }
  _initPromise = doInit();
  await _initPromise;
}

async function doInit(): Promise<void> {
  const wasmModule = await import("../wasm/velopack.js");

  function compileCore(path: string): any {
    const wasmPath = join(__dirname, "..", "wasm", path);
    const bytes = readFileSync(wasmPath);
    return new (globalThis as any).WebAssembly.Module(bytes);
  }

  const [cli, clocks, http, io, random]: any[] = await Promise.all([
    import("@bytecodealliance/preview2-shim/cli"),
    import("@bytecodealliance/preview2-shim/clocks"),
    import("@bytecodealliance/preview2-shim/http"),
    import("@bytecodealliance/preview2-shim/io"),
    import("@bytecodealliance/preview2-shim/random"),
  ]);

  // Pass host environment variables through to WASI
  if (cli._setEnv) {
    cli._setEnv(process.env);
  }

  const wasiImports: Record<string, any> = {
    "wasi:cli/environment": cli.environment,
    "wasi:cli/exit": cli.exit,
    "wasi:cli/stderr": cli.stderr,
    "wasi:cli/stdin": cli.stdin,
    "wasi:cli/stdout": cli.stdout,
    "wasi:cli/terminal-input": cli.terminalInput,
    "wasi:cli/terminal-output": cli.terminalOutput,
    "wasi:cli/terminal-stderr": cli.terminalStderr,
    "wasi:cli/terminal-stdin": cli.terminalStdin,
    "wasi:cli/terminal-stdout": cli.terminalStdout,
    "wasi:clocks/monotonic-clock": clocks.monotonicClock,
    "wasi:clocks/wall-clock": clocks.wallClock,
    "wasi:http/outgoing-handler": http.outgoingHandler,
    "wasi:http/types": http.types,
    "wasi:io/error": io.error,
    "wasi:io/poll": io.poll,
    "wasi:io/streams": io.streams,
    "wasi:random/insecure-seed": random.insecureSeed,
    "wasi:random/random": random.random,
  };

  _instance = wasmModule.instantiate(compileCore, {
    ...wasiImports,
    "velopack:core/host-process": hostProcess,
    "velopack:core/host-filesystem": hostFilesystem,
    "velopack:core/host-logger": hostLogger,
    "velopack:core/progress": progress,
  });
}

export function getWasm(): any {
  if (!_instance) {
    throw new Error(
      "Velopack WASM not initialized. Call await loadVelopack() first.",
    );
  }
  return _instance;
}

export { setProgressCallback } from "./progress.js";
export { setLoggerCallback } from "./host-logger.js";
