import {proxy} from "@neon-rs/load";
import {createRequire} from "module";
import * as path from "path";

const nodeRequire = createRequire(__filename);

function loadNative(name: string): any {
    const absolutePath = path.resolve(__dirname, "native", name);
    return nodeRequire(absolutePath);
}

module.exports = proxy({
    platforms: {
        "win32-x86-msvc": () => loadNative("velopack_nodeffi_win_x86_msvc.node"),
        "win32-x64-msvc": () => loadNative("velopack_nodeffi_win_x64_msvc.node"),
        "win32-arm64-msvc": () => loadNative("velopack_nodeffi_win_arm64_msvc.node"),
        "darwin-x64": () => loadNative("velopack_nodeffi_osx.node"),
        "darwin-arm64": () => loadNative("velopack_nodeffi_osx.node"),
        "linux-x64-gnu": () => loadNative("velopack_nodeffi_linux_x64_gnu.node"),
        "linux-arm64-gnu": () => loadNative("velopack_nodeffi_linux_arm64_gnu.node"),
    },
    debug: () => nodeRequire("../index.node"),
});
