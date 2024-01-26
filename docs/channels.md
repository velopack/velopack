| [docs](.) / channels.md |
|:---|

# Release Channels
*Applies to: Windows, MacOS, Linux*

Channels is a fundemental part of how Velopack understands and builds releases. Every release must belong to a channel. If you do not specify a channel when building a release (via the `--channel`) argument, the default channel will be the name of the target Operating System (eg. `win`, `osx`, or `linux`). 

When building releases, Velopack will create a `releases.{channel}.json` file, that should be uploaded with your other assets (eg. `.nupkg`). This is how `UpdateManager` knows what releases are available.

In general, you should not provide a channel to the `UpdateManager` constructor (leave it null). In this case, it will only search for update packages in the same channel that the current release was built for. For example, if you provided the `--channel stable` argument to `vpk`, and installed your app, then `UpdateManager` will automatically be searching for the file `releases.stable.json` when checking for updates.

❗For legacy purposes, Velopack will also generate a `RELEASES` file (for the `win` channel), or a `RELEASES-{channel}` file (for any other channel). By deploying these files as well as the `releases.{channel}.json` will allow legacy apps to upgrade to Velopack. If you do not have any users on legacy versions of your software, you can ignore these files.

## Switching channels in installed apps
It is often desirable to allow users to switch channels easily. For example, if your users downloaded an installer for a "stable" version of your app, they will only receive updates for the "stable" channel. Later on, they decide they wish to switch to the "beta" channel to try some experimental features in your app. 

This can be done by supplying a non-null channel argument to the UpdateManager constructor. So you would instantiate as `new UpdateManager("https://the.place/you-store/updates", "beta")` and then perform an update process as usual.

## Deploying cross-platform apps

It's important when deploying cross platform (or cross-architecture) apps that every unique os/rid has it's own channel. It wouldn't be good if your Windows app tried to install an OSX package etc!

The default channels are, `win`, `osx`, or `linux`, so if you are only distributing one release per platform, you do not need to specify a channel argument, everything should work automatically. If you are distributing feature channels (eg. 'stable', 'beta') or need to distribute multiple versions of your app per os (eg. `win-x64`, `win-arm64`) then you will need to define a channel strategy that does not collide. 

For example, if I was distributing an app on windows and osx which needed to support x64, and arm64, and also needed to support "stable" and "beta", then I would need the following 8 channels:
- win-x64-stable
- win-x64-beta
- win-arm64-stable
- win-arm64-beta
- osx-x64-stable
- osx-x64-beta
- osx-arm64-stable
- osx-arm64-beta

## Renaming a channel
You can't rename a channel per-say, but you can supercede it (ie. force all your users to switch to the new channel). Imagine you have been publishing an app that only supports x64 windows to the channel `stable` until now, but you now would like to release an arm64 version of your app. So you want to migrate all the users on `stable` to `win-x64`, while also creating a new channel named `win-arm64`. 

You should publish your next update (say v2.0.0) using `--channel win-x64`, which will create a new `releases.win-x64.json` file. You can now copy this file and rename it to `releases.stable.json` and deploy both files along with your v2.0.0 `.nupkg` to your update server. Any users on the "stable" channel will find the `releases.stable.json` file and update to your v2.0.0 win-x64 release, and once done will search for future updates at `releases.win-x64.json`. You only need to do this once, you will not need to update the `releases.stable.json` file again, however you may not want to delete it so users who have not opened your app in some time can still find the new updates.