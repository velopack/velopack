import { proxy } from "@neon-rs/load";

module.exports = proxy({
  platforms: {
    "win32-x64-msvc": () => require("./native/velopack_nodeffi_win_x64_msvc.node"),
    "win32-arm64-msvc": () => require("./native/velopack_nodeffi_win_arm64_msvc.node"),
    "darwin-x64": () => require("./native/velopack_nodeffi_osx.node"),
    "darwin-arm64": () => require("./native/velopack_nodeffi_osx.node"),
    "linux-x64-gnu": () => require("./native/velopack_nodeffi_linux_x64_gnu.node"),
    "linux-arm64-gnu": () => require("./native/velopack_nodeffi_linux_arm64_gnu.node"),
  },
});
