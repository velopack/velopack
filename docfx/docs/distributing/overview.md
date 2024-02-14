*Applies to: Windows, MacOS, Linux*

# Distributing Overview
Distributing with Velopack is extremely easy, it's usually just as simple as uploading your files somewhere that can be downloaded with HTTP. This means you could host them on an IIS or nodejs site, on shared file hosting such as AWS S3, Azure Storage, BackBlaze B2, or even for free on GitHub/GitLab releases if your project is open source.

The general steps for creating and deploying a Velopack release are:
1. Download the latest published release (eg. 1.0.0).
0. Run `vpk pack` to create your new release (eg. 1.0.1).
0. Upload your newly created 1.0.1 assets.
0. Update the remote releases.{channel}.json to reflect the newly uploaded assets.

See also: [Deployment commands](deploy-cli.md) can make this process much easier.

## List of assets produced
After packing a release with Velopack, you should have something like the following in your output directory:

```
Releases
├── YourAppId-1.0.1-full.nupkg
├── YourAppId-1.0.1-delta.nupkg
├── YourAppId-Setup.exe
├── YourAppId-Portable.zip
├── releases.{channel}.json
├── assets.{channel}.json
└── RELEASES
```

### Full and delta nupkg's
These are the update packages that installed applications use to find/install the latest version. Full packages contain an entire replication of your input files, plus some files Velopack adds. A delta package is a diff from the previously created full package. You need to have the previous version (eg. 1.0.0 in the above example) downloaded and in the output directory for a delta to be created (in this case, `1.0.0->1.0.0`). There are helpful [deployment commands](deploy-cli.md) which can download the latest version for you, so that deltas will be generated automatically. 

You must distribute these packages in the same folder as the `releases.{channel}.json` file for updates to work. 

### Setup and portable
This is what your user should download and run to install your app. On MacOS, you'll get a `.pkg` instead of a `-Setup.exe`. On Linux, there is no setup produced - only a portable `.AppImage`. The reason  for this is that `.AppImage`'s are completely portable to any relatively recent distro of linux.

### Release feed (`releases.{channel}.json`)
This file should be distributed in the same folder as the `nupkg` files are deployed. It contains a list of all available releases. 

When you provide a HTTP url to `UpdateManager`, it will search for this file. For example, if you `new UpdateManager("https://the.place/you-host/updates")`, then UpdateManager will request for `https://the.place/you-host/updates/releases.{channel}.json`. The channel UpdateManager uses in the request is automatic, you can [read more here about channels](../packaging/channels.md). 

For example, if you packed `1.0.0` and then `1.0.1` immediately after, the contents of this file might look like:

```json
{
  "Assets": [
    {
      "PackageId": "YourAppId",
      "Version": "1.0.1",
      "Type": "Full",
      "FileName": "YourAppId-1.0.1-full.nupkg",
      "SHA1": "537EC0F4E1C4263A230353FAB4150216E5AF3724",
      "Size": 1588612
    },
    {
      "PackageId": "YourAppId",
      "Version": "1.0.1",
      "Type": "Delta",
      "FileName": "YourAppId-1.0.1-delta.nupkg",
      "SHA1": "9615D266DDBCADF3B9CD82BABF9DA571A0EE2B83",
      "Size": 3606
    },
    {
      "PackageId": "YourAppId",
      "Version": "1.0.0",
      "Type": "Full",
      "FileName": "YourAppId-1.0.0-full.nupkg",
      "SHA1": "69122BABCEEEF9F653BFE59D87DDAEF363F9476F",
      "Size": 1588613
    }
  ]
}
```

The releases file should always mirror what files are _actually available_ in the remote folder that contains the releases file. So if you delete a nupkg release from the remote server, you should delete it from your remote release file too. If you are deploying newly created local files to a remote server which already contains some releases, then you should copy the assets from your local file to the remote releases file. 

> [!WARNING]
> This file is the only way that UpdateManager can discover releases, if you do not update it properly it may result in your users not getting updates.

It is tedious to update this file manually, so Velopack CLI provides deployment commands which can deploy assets and update this file automatically for you, as well as apply rentention policies around the number of releases to keep. [[Read more]](deploy-cli.md)

### Legacy release feed (`RELEASES`)
This releases format was used by Clowd.Squirrel and Squirrel.Windows, and is still produced by Velopack to allow you to migrate an application using one of those frameworks to Velopack. If you do not have any legacy users which need to migrate to Velopack, you can safely ignore this file.

### Assets file
This file contains a list of assets produced by the latest `pack` command. It is used by the [Velopack deployment commands](deploy-cli.md) to know which files should be uploaded. It can be ignored / deleted if you do not intend to use these commands to deploy releases and automatically update your release feed.