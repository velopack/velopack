| [docs](.) |
|:---|

# Velopack Documentation
🚧🚧This documentation is still under construction.🚧🚧

## FAQ
 - **My application was detected as a virus?** <br/>
   Velopack can't help with this, but you can [code-sign](signing.md) your app and check [other suggestions here](https://github.com/clowd/Clowd.Squirrel/issues/28#issuecomment-1016241760).
 - **What happened to SquirrelAwareApp? / Shortcuts** <br/>
   This concept no longer exists in Velopack. You can create hooks on install/update in a similar way using the `VelopackApp` builder. Although note that creating shortcuts or registry entries yourself during hooks is no longer required.
 - **Can Velopack bootstrap new runtimes during updates?** <br/>
   Yes, this is fully supported. Before installing updates, Velopack will prompt the user to install any missing updates.

## Using Velopack
- [Migrating to Velopack](migrating.md)
- [Logging & Debugging](debugging.md)
- [Command Line Reference](cli.md)
- Packaging Releases
  - Overview
  - Release Channels
  - Installer Overview & Customisation
  - [Code Signing](signing.md)
  - [Boostrapping frameworks (.NET, .Net Framework, VCRedist, etc)](bootstrapping.md)
  - Specify app RID / supported OS versions / supported architecture
- Distributing Releases
  - Overview
  - CI / CD Tips & Examples
  - Deploying to GitHub Releases
  - Deploying to Amazon S3 Storage (or compatible, eg. B2, Linode)
- Updating
  - Overview
  - Rolling back to a previous release
  - [Windows Shortcuts](shortcuts.md)
  - Customising updates (AfterInstall, BeforeUninstall, BeforeUpdate, AfterUpdate hooks)
