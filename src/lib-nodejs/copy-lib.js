const os = require("node:os");
const fs = require("node:fs");
const path = require("node:path");

const platform = os.platform();

if (platform == "win32") {
  fs.copyFileSync("../../target/debug/velopack_nodeffi.dll", "lib/debug.node");
} else if (platform == "darwin") {
  fs.copyFileSync("../../target/debug/libvelopack_nodeffi.dylib", "lib/debug.node");
} else if (platform == "linux") {
  fs.copyFileSync("../../target/debug/libvelopack_nodeffi.so", "lib/debug.node");
} else {
  throw new Error("Unsupported platform: " + platform);
}
