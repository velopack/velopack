const os = require("node:os");
const fs = require("node:fs");
const path = require("node:path");

const platform = os.platform();

function makeCopies(input) {
  // this is obviously not correct for a "production" bundle, but it is used for testing
  // creating a "node" module at all of the expected production locations allows us to test
  // webpack require resolution and native module bundling etc.
  if (!fs.existsSync("./lib/native")) fs.mkdirSync("./lib/native");
  if (!fs.existsSync("./src/native")) fs.mkdirSync("./src/native");
  fs.copyFileSync(input, "./lib/native/velopack_nodeffi_win_x86_msvc.node");
  fs.copyFileSync(input, "./lib/native/velopack_nodeffi_win_x64_msvc.node");
  fs.copyFileSync(input, "./lib/native/velopack_nodeffi_win_arm64_msvc.node");
  fs.copyFileSync(input, "./lib/native/velopack_nodeffi_osx.node");
  fs.copyFileSync(input, "./lib/native/velopack_nodeffi_linux_x64_gnu.node");
  fs.copyFileSync(input, "./lib/native/velopack_nodeffi_linux_arm64_gnu.node");
  fs.copyFileSync(input, "./src/native/velopack_nodeffi_win_x86_msvc.node");
  fs.copyFileSync(input, "./src/native/velopack_nodeffi_win_x64_msvc.node");
  fs.copyFileSync(input, "./src/native/velopack_nodeffi_win_arm64_msvc.node");
  fs.copyFileSync(input, "./src/native/velopack_nodeffi_osx.node");
  fs.copyFileSync(input, "./src/native/velopack_nodeffi_linux_x64_gnu.node");
  fs.copyFileSync(input, "./src/native/velopack_nodeffi_linux_arm64_gnu.node");
}

if (platform == "win32") {
  makeCopies("../../target/debug/velopack_nodeffi.dll");
} else if (platform == "darwin") {
  makeCopies("../../target/debug/libvelopack_nodeffi.dylib");
} else if (platform == "linux") {
  makeCopies("../../target/debug/libvelopack_nodeffi.so");
} else {
  throw new Error("Unsupported platform: " + platform);
}
