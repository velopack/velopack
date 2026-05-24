import { execSync } from "node:child_process";
import { mkdirSync, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

const wasmDir = join(__dirname, "..", "src", "wasm");
const libWasmTarget = join(
  __dirname,
  "..",
  "..",
  "lib-wasm",
  "target",
  "wasm32-wasip2",
  "debug",
  "velopack_wasm.wasm",
);

mkdirSync(wasmDir, { recursive: true });

if (!existsSync(libWasmTarget)) {
  console.error(`WASM file not found at: ${libWasmTarget}`);
  console.error(
    "Run 'cargo build' in src/lib-wasm first (with wasm32-wasip2 target).",
  );
  process.exit(1);
}

console.log("Transpiling WASM component to JS...");
try {
  execSync(
    `npx jco transpile "${libWasmTarget}" --out-dir "${wasmDir}" --name velopack --instantiation sync --no-typescript`,
    { stdio: "inherit" },
  );
} catch (e) {
  console.error("jco transpile failed.");
  process.exit(1);
}

console.log("WASM transpilation complete!");
