# Velopack
Velopack is a setup / installation framework for cross-platform dotnet applications. Great out-of-the-box development experience, with zero configuration or setup needed. Lightning fast to use, and lightning fast for your users, too.

## Features

- 😍 **Zero config** – Velopack takes your dotnet build output (eg. `dotnet publish`), and generates an installer, and update package in a single command.
- 🎯 **Cross platform** – Velopack supports building packages for Windows and OSX, with Linux on the way. No matter your target, Velopack can create a release in just one command.
- 🚀 **Automatic migrations** - If you are coming from [Squirrel.Windows](https://github.com/Squirrel/Squirrel.Windows) or [Clowd.Squirrel](https://github.com/clowd/Clowd.Squirrel), Velopack will automatically migrate your application. Just build your Velopack release and deploy! [Read more.](docs/migrating.md)
- ⚡️ **Lightning fast** – Velopack is written in Rust for native performance. Creating releases is multi-threaded, and produces delta packages for fast app updates. Applying update packages is highly optimised, and often can be done in the background.

## Getting Started

Please visit our GitHub for up to date documentation on how to use Velopack: https://github.com/velopack/velopack