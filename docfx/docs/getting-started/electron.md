*Applies to: Windows, MacOS, Linux*

# Getting Started: JS / Electron
This quick start is slightly tailored to Electron, so if you are using pure nodejs and bundling your app using [pkg](https://github.com/vercel/pkg) or similar you can ignore those steps.

1. Add Velopack to your `package.json`:
```txt
npm install velopack
```

2. Add the following code to your entry point (eg. `index.js`) as early as possible (before any electron startup code etc.):
```js
const { VelopackApp } = require('velopack');

// Velopack builder needs to be the first thing to run in the main process.
// In some cases, it might quit/restart the process to perform tasks.
VelopackApp.build().run();

// ... your other app startup code here
```

3. Add auto-updates somewhere to your app:
```js
const { UpdateManager } = require('velopack');

async function updateApp()
{
    const um = new UpdateManager();
    um.setUrlOrPath("https://the.place/you-host/updates");

    const updateInfo = await um.checkForUpdatesAsync();
    if (!updateInfo) {
        return; // no update available
    }

    await um.downloadUpdatesAsync(updateInfo.targetFullRelease, p => {
        console.log(`progress: ${p}%`);
    });

    um.applyUpdatesAndRestart(updateInfo.targetFullRelease);
}
```

4. If you are using electron/forge, you will need to add an asar unpack rule:
```js
module.exports = {
  packagerConfig: {
    asar: {
      // velopack contains native binaries which must remain unpacked
      unpack: '**/node_modules/velopack/**',
    },
  },
}
```

5. Compile your app to a binary (eg. `.exe` on Windows). Example using electron forge:
```sh
npx electron-forge package
```

6. Install the `vpk` command line tool:
```sh
dotnet tool update -g vpk
```
***Note: you must have the .NET Core SDK 6 installed to use and update the `vpk`***

7. Package your Velopack release / installers:
```sh
vpk pack -u MyAppUniqueId -v 1.0.0 -p /myBuildDir -e myexename.exe
```

✅ You're Done! Your app now has auto-updates and an installer.
You can upload your release to your website, or use the `vpk upload` command to publish it to the destination of your choice.