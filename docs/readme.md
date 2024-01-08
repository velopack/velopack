| [docs](.) |
|:---|

# Velopack Documentation
🚧🚧This documentation is still under construction.🚧🚧

## General
- [FAQ](#faq)
- [Migrating to Velopack](migrating.md)
- [Logging & Debugging](debugging.md)
- [Command Line Reference](cli.md)

## Using Velopack
- Packaging Releases
  - Overview
  - Release Channels & RID's
  - Installer Overview & Customisation
  - [Code Signing](signing.md)
  - Boostrapping frameworks (.NET, .Net Framework, VCRedist, etc)
- Distributing Releases
  - Overview
  - CI / CD Tips & Examples
  - Deploying to GitHub Releases
  - Deploying to Amazon S3 Storage (or compatible, eg. B2, Linode)
- Updating
  - Overview
  - Partial roll-out (A/B testing)
  - Rolling back to a previous release
  - Windows Shortcuts

## FAQ
 - **My application was detected as a virus?** <br/>
   Velopack can't help with this, but you can [code-sign](signing.md) your app and check [other suggestions here](https://github.com/clowd/Clowd.Squirrel/issues/28#issuecomment-1016241760).
 - **What happened to SquirrelAwareApp?** <br/>
   This concept no longer exists in Velopack. You can initialise hooks on install/update in a similar way using the `VelopackApp` builder.
 - **Can Velopack bootstrap new runtimes during updates?** <br/>
   Yes, this is fully supported. Before installing updates, Velopack will prompt the user to install any missing updates.
