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
//!
//! fn update_my_app() {
//!     let source = sources::HttpSource::new("https://the.place/you-host/updates");
//!     let um = UpdateManager::new(source, None, None).unwrap();
//!
//!     if let UpdateCheck::UpdateAvailable(updates) = um.check_for_updates().unwrap() {
//!         // there was an update available. Download it.
//!         um.download_updates(&updates, None).unwrap();
//!
//!         // download completed, let's restart and update
//!         um.apply_updates_and_restart(&updates).unwrap();
//!     }
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
//! ***Note: you must have the .NET Core SDK 8 installed to use and update `vpk`***
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
mod manager;
mod util;

// #[cfg(feature = "file-logging")]
mod file_rotate;

/// Utility functions for loading and working with Velopack bundles and manifests.
pub mod bundle;

/// Utility function for downloading files with progress reporting.
pub mod download;

/// Constant strings used internally by Velopack.
pub mod constants;

/// Locator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
pub mod locator;

/// Sources contains abstractions for custom update sources (eg. url, local file, github releases, etc).
pub mod sources;

/// Acquire and manage file-system based lock files.
pub mod lockfile;

/// Logging utilities and setup.
pub mod logging;

pub use app::*;
pub use manager::*;

#[macro_use]
extern crate log;

#[derive(thiserror::Error, Debug)]
#[allow(missing_docs, clippy::large_enum_variant)]
pub enum NetworkError
{
    #[error("Http error: {0}")]
    Http(#[from] ureq::Error),
    #[error("Url error: {0}")]
    Url(#[from] url::ParseError),
}

#[derive(thiserror::Error, Debug)]
#[allow(missing_docs)]
pub enum Error
{
    #[error("File does not exist: {0}")]
    FileNotFound(String),
    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),
    #[error("Checksum did not match for {0} (expected {1}, actual {2})")]
    ChecksumInvalid(String, String, String),
    #[error("Size did not match for {0} (expected {1}, actual {2})")]
    SizeInvalid(String, u64, u64),
    #[error("Zip error: {0}")]
    Zip(#[from] zip::result::ZipError),
    #[error("Network error: {0}")]
    Network(Box<NetworkError>),
    #[error("Json error: {0}")]
    Json(#[from] serde_json::Error),
    #[error("Semver parse error: {0}")]
    Semver(#[from] semver::Error),
    #[error("This application is missing a package manifest (.nuspec) or it could not be parsed.")]
    MissingNuspec,
    #[error("This application is missing a required property in its package manifest: {0}")]
    MissingNuspecProperty(String),
    #[error("This application is missing an Update.exe/UpdateNix/UpdateMac binary.")]
    MissingUpdateExe,
    #[error("This application is not properly installed: {0}")]
    NotInstalled(String),
    #[error("Generic error: {0}")]
    Generic(String),
}

impl From<url::ParseError> for Error {
    fn from(err: url::ParseError) -> Self {
        Error::Network(Box::new(NetworkError::Url(err)))
    }
}

impl From<ureq::Error> for Error {
    fn from(err: ureq::Error) -> Self {
        Error::Network(Box::new(NetworkError::Http(err)))
    }
}