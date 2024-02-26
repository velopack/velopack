*Applies to: Windows, MacOS, Linux*

# Getting Started: Rust

1. Add Velopack to your `Cargo.toml`:
```toml
[dependencies]
velopack = { version = "0.0", features = ["async"] } # Replace with actual version and desired features
```

2. Add the following code to your `main()` function:
```rust
use velopack::*;
fn main() {
    // VelopackApp should be the first thing to run 
    // In some circumstances it may terminate/restart the process to perform tasks.
    VelopackApp::build().run();
    
    // ... your other app startup code here
}
```

3. Add auto-updates somewhere to your app:
```rust
use velopack::*;
use anyhow::Result;
fn update_my_app() -> Result<()> {
    let um = UpdateManager::new("https://the.place/you-host/updates", None)?;
    let updates: Option<UpdateInfo> = um.check_for_updates()?;
    if updates.is_none() {
        return Ok(()); // no updates available
    }
    let updates = updates.unwrap();
    um.download_updates(&updates, |progress| { 
        println!("Download progress: {}%", progress);
    })?;
    um.apply_updates_and_restart(&updates, RestartArgs::None)?;
    Ok(())
}
```

4. Build your app with cargo:
```sh
cargo build --release
```

5. Package your Velopack release / installers:
```sh
vpk pack -u MyAppUniqueId -v 1.0.0 -p /target/release -e myexename.exe
```

✅ You're Done! Your app now has auto-updates and an installer. 
You can upload your release to your website, or use the `vpk upload` command to publish it to the destination of your choice.