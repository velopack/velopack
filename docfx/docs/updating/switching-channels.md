*Applies to: Windows, MacOS, Linux*

# Switching Channels
By default, `UpdateManager` will try and search for releases in the same channel that it was built for. You can [read more about packaging channels here](../packaging/channels.md). So normally, you should not provide a channel at all to the `UpdateManager` constructor.

However, from time to time, it may be useful to allow a user to switch channels without re-installing the application. For example, a user opts into getting "beta" features via your application settings. In that case, you can provide the channel explicitly:

```cs
new UpdateManager("https://the.place/you-host/updates", new UpdateOptions { ExplicitChannel = "beta" });
```

Also by default, the UpdateManager will only update to versions which are newer than the current version, leading to suboptimal behavior because often you may be switching to a version which is lower than the current version. Imagine the following scenario:

- You publish 2.0.0 to the `stable` channel.
- You publish 2.0.1 through 2.0.5 to the `beta` channel.
- Your user installs 2.0.0 `stable`, and then opts-in to `beta` via settings.
- Your user can update from 2.0.0 -> 2.0.5 fine, because 2.0.5 is a newer version.
- Your user encounters a bug and turns off `beta` via settings.
- By default, UpdateManager will not install stable 2.0.0 because it is a lower version than 2.0.5.

It's for this reason I recommend always using the `ExplicitChannel` option with the `AllowVersionDowngrade` option. For example:

```cs
new UpdateManager("https://the.place/you-host/updates", new UpdateOptions { 
    ExplicitChannel = "beta",
    AllowVersionDowngrade = true,
});
```
