# Velopack Documentation
🚧🚧This documentation is still under construction.🚧🚧

Velopack is an installation and auto-update framework for cross-platform .NET applications. It's opinionated, extremely easy to use with zero config needed. With just one command you can be up and running with an installable application, and it's lightning fast for your users, too.

Be sure to check us out on [GitHub](https://github.com/velopack/velopack) and [join our Discord](https://discord.gg/CjrCrNzd3F) for any questions/support!

## Overview
To enable your application to make full use of Velopack, you need to do 3 things:
1. Add the SDK to your app, and check for updates. [[Read more]](updating/overview.md)
0. Run the `vpk` command line tool to generate your update packages and installers. [[Read more]](packaging/overview.md)
0. Upload your release somewhere your app can download updates from. [[Read more]](distributing/overview.md)

> [!TIP]
> If you are migrating an application from Squirrel.Windows or Clowd.Squirrel, you may also want to read [the migrating guide](migrating.md).

## Quick Start
- [C# .NET Quick Start](getting-started/csharp.md)
- Sample App: [Avalonia Cross Platform](https://github.com/velopack/velopack/tree/master/examples/AvaloniaCrossPlat)
- Sample App: [WPF / .Net Framework](https://github.com/velopack/velopack/tree/master/examples/VeloWpfSample)

## FAQ
- **My application was detected as a virus?** <br/>
  Velopack can't help with this, but you can [code-sign](packaging/signing.md) your app and check [other suggestions here](https://github.com/clowd/lowd.Squirrel/issues/28#issuecomment-1016241760).
- **What happened to SquirrelAwareApp? / Shortcuts** <br/>
  This concept no longer exists in Velopack. You can create hooks on install/update in a similar way using the `VelopackApp` builder. Although note that reating shortcuts or registry entries yourself during hooks is no longer required.
- **Can Velopack bootstrap new runtimes during updates?** <br/>
  Yes, this is fully supported. Before installing updates, Velopack will prompt the user to install any missing updates.