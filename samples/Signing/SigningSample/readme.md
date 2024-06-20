# VeloWpfSample
_Prerequisites: vpk command line tool installed_

This app demonstrates how to the releses.json can be signed and verified.

Before you run this sample compile it and run the "ReleaseSigner" to generate keys

```shell

.\ReleaseSigner.exe generate rsa

copy *.pem ..\..\keys\
```

You can run this sample by executing the build script with a version number: `build.bat 1.0.0`. Once built, you can install the app - build more updates, and then test updates and so forth. The sample app will check the local release dir for new update packages. 

In your production apps, you should deploy your updates to some kind of update server instead.


## Implementation Notes

* The sample is based on the WPF Sample
* Make sure to keep backups of keys and never commit them to source control
* For production you might want to use some kind of secure key storage such as azure key vault
* Only include the public key in your app, never the private key
* Ensure you have a strategy for rotating keys (maybe even a valid backup key)


* This sample is mostly a POC you could as well use CMS (CMSSigner to create a signature, detacted or not) and use that instead
  *  By going with CMS you can leverate the OS certificate store and use that for trust instead of hardcoding keys
   certificate based trustd (maybe use your code signing certificate)
  * Hardcoded raw keys might make a good tradeoff for free / OSS software where you can't afford a code signing certificate