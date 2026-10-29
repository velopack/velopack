# Velopack
Velopack is an installation and auto-update framework for cross-platform applications. It's opinionated, extremely easy to use with zero config needed. With just one command you can be up and running with an installable application, and it's lightning fast for your users, too.

## Features

- 😍 **Zero config** – Velopack takes your compiler output and generates an installer, updates, delta packages, and self-updating portable package in just one command.
- 🎯 **Cross platform** – Velopack supports building packages for **Windows**, **OSX**, and **Linux**, so you can use one solution for every target.
- 🚀 **Automatic migrations** - If you are coming from other popular frameworks (eg. [Squirrel](https://github.com/Squirrel/Squirrel.Windows)), Velopack can automatically migrate your application.
- ⚡️ **Lightning fast** – Velopack is written in Rust for native performance. Delta packages mean your user only downloads what's changed between versions.
- 📔 **Language agnostic** - With support for C#, C++, JS, Rust and more. Use a familiar API for updates no matter what language your project is.
- 🌍 **Localized** – Translated into 39 languages across 6 continents, so your users feel at home no matter where they are.

## Documentation
- 📖 [Read the docs](https://docs.velopack.io/)
- ⚡ [Quick start guides](https://docs.velopack.io/category/getting-started)
- 🕶️ [View example apps](https://docs.velopack.io/category/sample-apps)
- 📺 [See website & demo](https://velopack.io/)

## MSBuild integration

Reference `Velopack.Build` to create release packages during `dotnet publish`. It includes the
`Velopack` runtime library transitively, so a separate `Velopack` package reference is not required.
The build task downloads and runs the exact `vpk` NuGet version resolved for the application's
`Velopack` reference (including prerelease versions), using `dotnet tool exec` and JSON options.
This requires the .NET 10 SDK or later.

The nearest `nuget.config` above the project is passed to the tool, or `RestoreConfigFile` when
explicitly set. For custom feeds, package source mappings must include both `Velopack*` and `vpk`.
`VelopackVpkVersion` explicitly overrides the tool version, and `VelopackVpkNugetSource` overrides
the tool's package sources. Set `VelopackPackOnPublish=false` to disable packaging.

## Community
- ❓ Ask questions, get support, or discuss ideas on [Discord](https://discord.gg/CjrCrNzd3F)
- 🗣️ Report bugs or request features on [GitHub Issues](https://github.com/velopack/velopack/issues)