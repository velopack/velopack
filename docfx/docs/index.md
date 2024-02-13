# Documentation
Velopack is an installation and auto-update framework for cross-platform .NET applications. It's opinionated, extremely easy to use with zero config needed. With just one command you can be up and running with an installable application, and it's lightning fast for your users, too.

Be sure to check us out on [GitHub](https://github.com/velopack/velopack) and [join our Discord](https://discord.gg/CjrCrNzd3F) for any questions/support!

## Overview
To enable your application to make full use of Velopack, you need to do 3 things:
1. Add the SDK to your app, and check for updates. [[Read more]](updating/overview.md)
0. Run the `vpk` command line tool to generate your update packages and installers. [[Read more]](packaging/overview.md)
0. Upload your release somewhere your app can download updates from. [[Read more]](distributing/overview.md)

## FAQ
- **My application was detected as a virus?** <br/>
  Velopack can't help with this, but you can [code-sign](packaging/signing.md) your app and check [other suggestions here](https://github.com/clowd/Clowd.Squirrel/issues/28#issuecomment-1016241760).
- **What happened to SquirrelAwareApp? / Shortcuts** <br/>
  This concept no longer exists in Velopack. You can create hooks on install/update in a similar way using the `VelopackApp` builder. Although note that reating shortcuts or registry entries yourself during hooks is no longer required.
- **Can Velopack bootstrap new runtimes during updates?** <br/>
  Yes, this is fully supported. Before installing updates, Velopack will prompt the user to install any missing updates.
- **How do I install the `vpk` tool? / I've installed the tool but it doesn't work**
  For now, you need to install `dotnet` runtime 6.0 or 8.0 for your platform, and then run `dotnet tool update -g vpk`. 
  If you get a message that it was installed successfully, but running it in your terminal results in a "binary/command not found" message,
  it's probably because your PATH is not set-up properly. For windows, `%USERPROFILE%\.dotnet\tools` should be on the PATH. For macos, [see this issue](https://github.com/dotnet/sdk/issues/9415).
- **Can I use a 4 part version (1.0.0.0) instead of SemVer2?**
  Velopack only supports a 3 part version with tags and metadata following (1.0.0-build.23+metadata), following the SemVer2 standard. Some people choose to version with the date, 2024.01.12 for example. It's also possible to get automated git commit based versioning [using something like nbgv](https://github.com/dotnet/Nerdbank.GitVersioning). The reason Velopack supports SemVer2 and not traditional 4 part versions is that it's possible to provide a lot more information in SemVer2 versions, and it is not feasible for us to support both formats throughout the framework.