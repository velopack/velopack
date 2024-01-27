<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docfx/images/velopack-white.svg">
  <img alt="Velopack Logo" src="docfx/images/velopack-black.svg" width="400">
</picture>

---

[![Nuget](https://img.shields.io/nuget/v/Velopack?style=flat-square)](https://www.nuget.org/packages/Velopack/)
[![Discord](https://img.shields.io/discord/767856501477343282?style=flat-square&color=purple)](https://discord.gg/CjrCrNzd3F)
[![Build](https://img.shields.io/github/actions/workflow/status/velopack/velopack/build.yml?branch=develop&style=flat-square)](https://github.com/velopack/velopack/actions)
[![Codecov](https://img.shields.io/codecov/c/github/velopack/velopack?style=flat-square)](https://app.codecov.io/gh/velopack/velopack)
[![License](https://img.shields.io/github/license/velopack/velopack?style=flat-square)](https://github.com/velopack/velopack/blob/develop/LICENSE)

Velopack is an installation and auto-update framework for cross-platform .NET applications. It's opinionated, extremely easy to use with zero config needed. With just one command you can be up and running with an installable application, and it's lightning fast for your users, too.

## Features

- 😍 **Zero config** – Velopack takes your dotnet build output (eg. `dotnet publish`), and generates an installer, and updates and delta packages in a single command.
- 🎯 **Cross platform** – Velopack supports building packages for **Windows**, **OSX**, and **Linux**. No matter your target, Velopack can create a release in just one command.
- 🚀 **Automatic migrations** - If you are coming from [Squirrel.Windows](https://github.com/Squirrel/Squirrel.Windows) or [Clowd.Squirrel](https://github.com/clowd/Clowd.Squirrel), Velopack will automatically migrate your application. Just build your Velopack release and deploy! [Read more.](docs/migrating.md)
- ⚡️ **Lightning fast** – Velopack is written in Rust for native performance. Creating releases is multi-threaded, and produces delta packages for ultra fast app updates. Applying update packages is highly optimised, and often can be done in the background.

https://github.com/velopack/velopack/assets/1287295/0ff1bea7-15ed-42ae-8bdd-9519f1033432

## Getting Started

This is a very simple example of the steps you would take to generate an installer and update packages for your application. Be sure to [read the documentation](docs) for an overview of more features!

1. Install the command line tool `vpk`:
   ```cmd
   dotnet tool update -g vpk
   ```
2. Install the  [Velopack NuGet Package](https://www.nuget.org/packages/velopack) in your main project:
   ```cmd
   dotnet add package Velopack
   ```
3. Configure your Velopack app at the beginning of `Program.Main`:
   ```cs
   static void Main(string[] args)
   {
       VelopackApp.Build().Run();
       // ... your other startup code below
   }
   ```
4. Publish dotnet and build your first Velopack release! 🎉
   ```cmd
   dotnet publish -c Release --self-contained -r win-x64 -o .\publish
   vpk pack -u YourAppId -v 1.0.0 -p .\publish -e yourMainApp.exe
   ```
5. Add automatic updating to your app:
   ```cs
   private static async Task UpdateMyApp()
   {
       var mgr = new UpdateManager("https://the.place/you-host/updates");

       // check for new version
       var newVersion = await mgr.CheckForUpdatesAsync();
       if (newVersion == null)
           return; // no update available

       // download new version
       await mgr.DownloadUpdatesAsync(newVersion);

       // install new version and restart app
       mgr.ApplyUpdatesAndRestart();
   }
   ```

If you're not sure how these instructions fit into your app, check the example apps for common scenarios such as WPF or Avalonia.

## Documentation
- 📖 [Read the docs](docs)
- 🕶️ [View example apps](examples)

## Community
- ❓ Ask questions, get support, or discuss ideas on [our Discord server](https://discord.gg/CjrCrNzd3F)
- 🗣️ Report bugs on [GitHub Issues](https://github.com/velopack/velopack/issues)

## Contributing
- 💬 Join us on [Discord](https://discord.gg/CjrCrNzd3F) to get involved in dev discussions
- 🚦 Read our [compiling guide](docs/compiling.md)

## Testimonials 
I have now got my external facing application using velopack. I am very impressed. Seems to work fabulously well and be much faster both in the initial build and in the upgrading of the software on the end user's  machine than Squirrel was. It's amazing and the best installer I've ever used in over 30 years of development. Thanks so much!  You are doing some great work!
[- Stefan (Discord)](https://discord.com/channels/767856501477343282/767856501477343286/1195642674078830613)