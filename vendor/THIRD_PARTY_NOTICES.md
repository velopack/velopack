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

### type2-runtime (continuous Mar 7, 2026)
- Type 2 AppImage runtime binaries needed to create a .AppImage for Linux
- Can be found at https://github.com/AppImage/type2-runtime
- License is MIT https://github.com/AppImage/type2-runtime/blob/main/LICENSE

### mksquashfs.exe (backhand v0.25.1)
- Creates squashfs filesystems on Windows for AppImage packaging
- Built on the backhand library: https://github.com/wcampbell0x2a/backhand
- License is MIT / Apache-2.0: https://github.com/wcampbell0x2a/backhand/blob/main/LICENSE-MIT

### WiX Toolset Code v7.0.0 (fork)
- Utility to create MSI installers on Windows
- Can be found at https://github.com/velopack/wix
- License is MS-RL https://github.com/velopack/wix/blob/v7/LICENSE.TXT
- This project is a fork and modification of the original WiX source code at https://github.com/wixtoolset/wix
- We rely on no binaries from the wixtoolset project, therefore the the wix OSMF EULA does not apply.

# Source code adapted from third parties
The following MIT licensed projects were used as references for source code in `src/vpk/Velopack.Packaging.Windows/Signing`.
Each adapted file names its source in a header comment.

### PowerShell-OpenAuthenticode
- PE Authenticode digest, WIN_CERTIFICATE handling and Authenticode ASN.1 structures (PeAuthenticode.cs, AuthenticodeAsn.cs)
- Can be found at https://github.com/jborean93/PowerShell-OpenAuthenticode
- License is MIT, Copyright (c) 2023 Jordan Borean: https://github.com/jborean93/PowerShell-OpenAuthenticode/blob/main/LICENSE

### dotnet/sign
- Azure Artifact Signing (Trusted Signing) RSA adapter pattern (RemoteRsa.cs)
- Can be found at https://github.com/dotnet/sign
- License is MIT, Copyright (c) .NET Foundation and Contributors: https://github.com/dotnet/sign/blob/main/LICENSE.txt

### AzureSignTool
- Static access token credential (AccessTokenCredential.cs). AzureSign.Core is also consumed as a NuGet package to sign MSI files.
- Can be found at https://github.com/vcsjones/AzureSignTool
- License is MIT, Copyright (c) 2017 Kevin Jones and Oren Novotny: https://github.com/vcsjones/AzureSignTool/blob/main/LICENSE
