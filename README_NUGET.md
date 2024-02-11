# Velopack
Velopack is an installation and auto-update framework for cross-platform .NET applications. It's opinionated, extremely easy to use with zero config needed. With just one command you can be up and running with an installable application, and it's lightning fast for your users, too.

## Features

- 😍 **Zero config** – Velopack takes your dotnet build output (eg. `dotnet publish`), and generates an installer, and update package in a single command.
- 🎯 **Cross platform** – Velopack supports building packages for Windows and OSX, and Linux. No matter your target, Velopack can create a release in just one command.
- 🚀 **Automatic migrations** - If you are coming from [Squirrel.Windows](https://github.com/Squirrel/Squirrel.Windows) or [Clowd.Squirrel](https://github.com/clowd/Clowd.Squirrel), Velopack will automatically migrate your application. Just build your Velopack release and deploy! [Read more.](https://velopack.io/docs/migrating.html)
- ⚡️ **Lightning fast** – Velopack is written in Rust for native performance. Creating releases is multi-threaded, and produces delta packages for ultra fast app updates. Applying update packages is highly optimised, and often can be done in the background.

## Getting Started

- Visit our GitHub: https://github.com/velopack/velopack
- Read the documentation: https://velopack.io/docs/