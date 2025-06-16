<picture>
  <source media="(prefers-color-scheme: dark)" srcset="artwork/velopack-white.svg">
  <img alt="Velopack Logo" src="artwork/velopack-black.svg" width="400">
</picture>

---

[![Nuget](https://img.shields.io/nuget/v/Velopack?style=flat-square&logo=nuget&logoColor=white)](https://www.nuget.org/packages/Velopack/)
[![Discord](https://img.shields.io/badge/chat-Discord-5865F2?style=flat-square&logo=discord&logoColor=white)](https://discord.gg/M6he8ZPAAJ)
[![Build](https://img.shields.io/github/actions/workflow/status/velopack/velopack/build.yml?branch=develop&style=flat-square&logo=github&logoColor=white)](https://github.com/velopack/velopack/actions)
[![Codecov](https://img.shields.io/codecov/c/github/velopack/velopack?style=flat-square&logo=codecov&logoColor=white)](https://app.codecov.io/gh/velopack/velopack)
[![License](https://img.shields.io/github/license/velopack/velopack?style=flat-square)](https://github.com/velopack/velopack/blob/develop/LICENSE)


Velopack is an installation and auto-update framework for cross-platform applications. It's opinionated, extremely easy to use with zero config needed. With just one command you can be up and running with an installable application, and it's lightning fast for your users, too.

## Features

- 😍 **Zero config** - Velopack takes your compiler output and generates an installer, updates, delta packages, and self-updating portable package in just one command.
- 🎯 **Cross platform** - Velopack supports building packages for **Windows**, **OSX**, and **Linux**, so you can use one solution for every target.
- 🚀 **Automatic migrations** - If you are coming from other popular frameworks (eg. [Squirrel](https://github.com/Squirrel/Squirrel.Windows)), Velopack can automatically migrate your application.
- ⚡️ **Lightning fast** - Velopack is written in Rust for native performance. Delta packages mean your user only downloads what's changed between versions.
- 📔 **Language agnostic** - With support for C#, C++, JS, Rust and more. Use a familiar API for updates no matter what language your project is.

https://github.com/velopack/velopack/assets/1287295/0ff1bea7-15ed-42ae-8bdd-9519f1033432

## Documentation
- 📖 [Read the docs](https://docs.velopack.io/)
- ⚡ [Quick start guides](https://docs.velopack.io/category/getting-started)
- 🕶️ [View example apps](https://docs.velopack.io/category/sample-apps)
- 📺 [See website & demo](https://velopack.io/)

## Community
- ❓ Ask questions, get support, or discuss ideas on [Discord](https://discord.gg/CjrCrNzd3F)
- 🗣️ Report bugs or request features on [GitHub Issues](https://github.com/velopack/velopack/issues)

## Contributing
- 💬 Join us on [Discord](https://discord.gg/CjrCrNzd3F) to get involved in dev discussions
- 🚦 Read our [contributing guide](https://docs.velopack.io/category/contributing)

## Testimonials 
I have now got my external facing application using velopack. I am very impressed. Seems to work fabulously well and be much faster both in the initial build and in the upgrading of the software on the end user's  machine than Squirrel was. It's amazing and the best installer I've ever used in over 30 years of development. Thanks so much!  You are doing some great work!
[- Stefan (Discord)](https://discord.com/channels/767856501477343282/767856501477343286/1195642674078830613)

Just wanted to say a huge thank you. I've been using Clowd.Squirrel for a couple of years now since Squirrel.Windows wasn't working for me. Just popped into that repository today to look for some documentation and noticed the release of Velopack. How unexpected! It works fantastic and is so much faster, wow! Thank you again, the amount of work that went into both Clowd.Squirrel and Velopack is staggering. It's very appreciated.
[- Kizari (Discord)](https://discord.com/channels/767856501477343282/767856501477343286/1200837489640878180)

I've used a lot of installer frameworks and Velopack is by far the best. Everything is like magic: you run the installer, and then the app is just open, ready to use. Updates apply and relaunch in ~2 seconds with no UAC prompts. The installer creation process is painless and integrates easily with code signing, and the command-line tool makes it simple to upload your updater files. You don't need to futz with separate installer scripts in some weird language; you can build all those hooks into your main app! The support is also phenominal; every concern I've had has been addressed. This is the future of desktop installers.
[- RandomEngy (Discord)](https://discord.com/channels/767856501477343282/947444323765583913/1200897478036299861)

I'm extremely impressed with Velopack's performance in creating releases, as well as checking for and applying updates. It is significantly faster than other tools. The vpk CLI is intuitive and easy to implement, even with my complex build pipeline. Thanks to Velopack, I've been able to streamline my workflow and save valuable time. It's a fantastic tool that I highly recommend!
[- khdc (Discord)](https://discord.com/channels/767856501477343282/947444323765583913/1216460920696344576)
