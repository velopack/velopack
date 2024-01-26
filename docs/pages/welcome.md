# Velopack Documentation
🚧🚧This documentation is still under construction.🚧🚧

## FAQ
 - **My application was detected as a virus?** <br/>
   Velopack can't help with this, but you can [code-sign](packaging/signing.md) your app and check [other suggestions here](https://github.com/clowd/Clowd.Squirrel/issues/28#issuecomment-1016241760).
 - **What happened to SquirrelAwareApp? / Shortcuts** <br/>
   This concept no longer exists in Velopack. You can create hooks on install/update in a similar way using the `VelopackApp` builder. Although note that creating shortcuts or registry entries yourself during hooks is no longer required.
 - **Can Velopack bootstrap new runtimes during updates?** <br/>
   Yes, this is fully supported. Before installing updates, Velopack will prompt the user to install any missing updates.

