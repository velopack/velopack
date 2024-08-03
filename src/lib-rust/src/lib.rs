//! # Velopack
//! [Velopack](https://velopack.io) is a auto-update and installation framework for cross-platform desktop applications.
//! With less than 10 lines of code, you can add auto-update and installation features to your application.
//!
//! ## Features
//! - 😍 **Zero config** – Velopack takes your build output (eg. `cargo build`), and generates an installer, and updates and delta packages in a single command.
//! - 🎯 **Cross platform** – Velopack supports building packages for **Windows**, **OSX**, and **Linux**. No matter your target, Velopack can create a release in just one command.
//! - ⚡️ **Lightning fast** – Velopack is written in Rust for native performance. Creating releases is multi-threaded, and produces delta packages for ultra fast app updates. Applying update packages is highly optimised, and often can be done in the background.
//!
//! ## Documentation
//! The documentation in this rust crate is minimal and only covers the create itself, so it's highly recommended that you also
//! read the main Velopack documentation at [https://docs.velopack.io](https://docs.velopack.io).
//!
//! ## Components
//! - **this crate**: The core library that provides auto-update features and glue to the other components.
//! - **vpk cli tool**: The `vpk` command line tool packages and publishes your releases and installers.
//! - **update binary**: Bundled with your application by vpk, handles
//!
//! ## Optional Rust Features
//! - `async`: Enables async support using async-std.
//!
//! ## Quick Start
//! 1. Add Velopack to your `Cargo.toml`:
//! ```toml
//! [dependencies]
//! velopack = { version = "0.0", features = ["async"] } # Replace with actual version and desired features
//! ```
//!
//! 2. Add the following code to your `main()` function:
//! ```rust
//! use velopack::*;
//!
//! fn main() {
//!     // VelopackApp should be the first thing to run, in some circumstances it may terminate/restart the process to perform tasks.
//!     VelopackApp::build().run();
//!     // Your other app startup code here
//! }
//! ```
//!
//! 3. Add auto-updates somewhere to your app:
//! ```rust
//! use velopack::*;
//! use anyhow::Result;
//!
//! fn update_my_app() -> Result<()> {
//!     let source = sources::HttpSource::new("https://the.place/you-host/updates");
//!     let um = UpdateManager::new(source, None)?;
//!     let updates: Option<UpdateInfo> = um.check_for_updates()?;
//!     if updates.is_none() {
//!         return Ok(()); // no updates available
//!     }
//!     let updates = updates.unwrap();
//!     um.download_updates(&updates, |progress| {
//!         println!("Download progress: {}%", progress);
//!     })?;
//!     um.apply_updates_and_restart(&updates, RestartArgs::None)?;
//!     Ok(())
//! }
//! ```
//!
//! 4. Build your app with cargo:
//! ```sh
//! cargo build --release
//! ```
//!
//! 5. Install the `vpk` command line tool:
//! ```sh
//! dotnet tool update -g vpk
//! ```
//! ***Note: you must have the .NET Core SDK 6 installed to use and update `vpk`***
//!
//! 6. Package your Velopack release / installers:
//! ```sh
//! vpk pack -u MyAppUniqueId -v 1.0.0 -p /target/release -e myexename.exe
//! ```
//!
//! ✅ You're Done! Your app now has auto-updates and an installer.
//! You can upload your release to your website, or use the `vpk upload` command to publish it to the destination of your choice.
//!
//! Read the Velopack documentation at [https://docs.velopack.io/](https://docs.velopack.io/) for more information.

#![warn(missing_docs)]

mod app;
mod bundle;
mod download;
mod manager;
mod manifest;
mod util;

/// Locator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
pub mod locator;
/// Sources contains abstractions for custom update sources (eg. url, local file, github releases, etc).
pub mod sources;

pub use app::*;
pub use manager::*;

#[macro_use]
extern crate anyhow;
#[macro_use]
extern crate log;
