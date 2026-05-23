# Vendor Binaries
This folder contains pre-compiled binaries from a variety of sources. These should be updated periodically.

### signtool.exe v10.0.22621.3233
- Signs application binaries while building packages.
- Can be found at https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.22621.3233 under bin\10.0.22621.0\x64
- License: https://aka.ms/WinSDKLicenseURL

### zstd.exe v1.5.5
- Fast compression and diff/patch
- Can be found at https://github.com/facebook/zstd
- License is GPL-2.0 & BSD 3: https://github.com/facebook/zstd/blob/dev/LICENSE, https://github.com/facebook/zstd/blob/dev/COPYING

### appimagekit (continuous Mar 8, 2023)
- Only include the "runtime" binaries needed to create a .AppImage for Linux
- Can be found at https://github.com/AppImage/AppImageKit
- License is MIT https://github.com/AppImage/AppImageKit/blob/master/LICENSE

### squashfs-tools-ng v1.3.0
- Squashfs utilities for Windows
- Can be found at https://github.com/AgentD/squashfs-tools-ng
- License is GPL-3 https://github.com/AgentD/squashfs-tools-ng/blob/master/COPYING.md

### WiX Toolset Code v7.0.0 (fork)
- Utility to create MSI installers on Windows
- Can be found at https://github.com/velopack/wix
- License is MS-RL https://github.com/velopack/wix/blob/v7/LICENSE.TXT
- This project is a fork and modification of the original WiX source code at https://github.com/wixtoolset/wix
- We rely on no binaries from the wixtoolset project, therefore the the wix OSMF EULA does not apply.
