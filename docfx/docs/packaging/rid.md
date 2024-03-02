*Applies to: Windows, MacOS, Linux*

# RID (Runtime Identifier)
Similar to how you provide a RID to dotnet to designate your target operating system and architecture, you can do the same for Velopack to tell it what your application supports. 

An RID is composed of three parts (`{os}{version?}-{arch}`)
 - os: operating system (`win`, `osx`, or `linux`)
 - version: optionally, specify minimum supported version (eg. `win7`, `win8.1`, `win10.0.18362`)
 - arch: optionaly, specify supported CPU architecture (eg.`win-x86`, `win-x64`, `win-arm64`)

If you were to provide the RID `--rid win10-arm64`, any users trying to install your app on Windows 7, 8, or 8.1 will receive a message saying their operating system is not supported. Similarly, if a Windows 11 user with an x64 cpu were trying to install - it would also fail with a helpful message.

If trying to target Windows 11, they did not increment the major build number from 10 to 11. Anything >= build 22000 is classified as Windows 11. For example:
 - `win11 == win10.0.22000`
 - `win11.0.22621 == win10.0.22621`

On MacOS, the RID (min version and arch) is just stored as metadata in the `.pkg` which will be handled natively by the operating system.

#### Also read
- [Windows 10 version history](https://en.wikipedia.org/wiki/Windows_10_version_history)
- [Windows 11 version history](https://en.wikipedia.org/wiki/Windows_11_version_history)
- [.NET RID Catalog](https://learn.microsoft.com/dotnet/core/rid-catalog)
