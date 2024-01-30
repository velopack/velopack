*Applies to: Windows, MacOS*

# Code Signing
Code signing is an essential part of application distribution. On Windows, applications without code signatures are likely to be flagged as viruses. On OSX, codesigning and Notarization is required before your application can be run by users.

On both platforms, signing needs to be performed by Velopack itself, this is because the Velopack binaries (such as Update and Setup) need to be signed at different points in the package build process.

## Signing on Windows

### Acquiring a code signing certificate
First, you need to acquire a code-signing certificate from a reputable brand. Microsoft lists the following trusted issuers [in their documentation](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-cert-manage):

- [Certum](https://shop.certum.eu/data-safety/code-signing-certificates/certum-ev-code-sigining.html)
- [SSL.com](https://www.ssl.com/certificates/ev-code-signing/)
- [DigiCert](https://www.digicert.com/signing/code-signing-certificates)
- [Entrust](https://www.entrustdatacard.com/products/digital-signing-certificates/code-signing-certificates)
- [GlobalSign](https://go.microsoft.com/fwlink/p/?LinkId=620888)
- [IdenTrust](https://www.identrust.com/digital-certificates/trustid-ev-code-signing)
- [Sectigo (formerly Comodo)](https://sectigo.com/ssl-certificates-tls/code-signing)
    
> [!TIP]
> Note that since June 1, 2023 there [has been a policy change](https://knowledge.digicert.com/general-information/new-private-key-storage-requirement-for-standard-code-signing-certificates-november-2022), meaning it is no longer possible to directly download your code signing certificate private key as a file from issuers. This means most signing options available today will require you to participate in Cloud Code Signing, or install your certificate to a HSM (usb / hardware module). Some issurs may allow you to install your cert to a pre-owned/generic HSM, and some may force you to purchase and ship their own brand of HSM. CI/CD may not be possible with some issuers unless they provide tools specifically designed to do that.

It may be possible to get official certificates from an authorised reseller at a cheaper price:
- [SignMyCode](https://signmycode.com/)
- [GoGetSSL](https://www.gogetssl.com/code-signing-ssl/)

If looking for a cheap option, at the time of writing Certum does an [Open Source Cloud Signing Certificate](https://certum.store/data-safety/code-signing-certificates.html?as_dane_w_certyfikacie=5720) for $58. Note that it is not possible to automate Certum certificates with CI/CD because they require you to install virtual signing drivers (SimpleSign Desktop) and sign in to it with their 2FA SimplySign app. 

> [!WARNING]
> This document is by no means a recommendation or advice to purchase from any particular code-signing certificate issuer. This is just general guidance for the process one might follow to purchase a certificate, and you must validate for yourself that the issuer you choose is fit for your use-case.

### Signing via `signtool.exe`
Usually signing is accomplished via `signtool.exe`. If you already use this tool to sign your application, you can just pass your sign parameters straight to Velopack (minus the 'sign' command).

For example, if your signing command before was:
```cmd
signtool.exe sign /td sha256 /fd sha256 /f yourCert.pfx /tr http://timestamp.comodoca.com
```

Then now with `--signParams` it would be:
```cmd
vpk pack ... --signParams "/td sha256 /fd sha256 /f yourCert.pfx /tr http://timestamp.comodoca.com"
```

If you are new to using `signtool.exe`, you can check the [command line reference here](https://learn.microsoft.com/en-us/dotnet/framework/tools/signtool-exe). I recommend getting signing working on a single binary first, using `signtool.exe`, before trying to get things working with the Velopack CLI.

If you are using a USB / HSM and have the following signing error `Error: SignerSign() failed." (-2147023673/0x800704c7)`, this means that you are missing a password / unlock token. This can be added to your signing command with the `/csp /k` parameters. [[Read more]](https://stackoverflow.com/questions/17927895/automate-extended-validation-ev-code-signing-with-safenet-etoken/54439759#54439759)

❗**Take care when providing parameters: If any have a space in a signing argument, you must wrap it with quotes and escape with a backslash.**

By default, Velopack will sign 10 files per call to `signtool.exe`, to speed up signing and reduce the number of times you need to interact with the console if you are using some kind of interactive signing method. This can be disabled with the `--signParallel 1` argument.

### Custom signing commands and tools
If you have more advanced signing requirements, such as a custom signing tool (eg. `AzureSignTool.exe`), then you can provide a command template instead, where `{{file}}` is the binary that Velopack will substitute and sign:

```cmd
vpk pack ... --signTemplate "AzureSignTool.exe sign ... {{file}}"
```

## Signing & Notarizing on OSX
Codesigning and Notarization is required before your application can be run by users, therefore it is a required step before deploying your application.

### Creating code signing certificates
1. First, you will need to create an account at https://developer.apple.com, pay the annual developer fee, and accept any license agreements. 
0. Navigate to your certificates: https://developer.apple.com/account/resources/certificates
0. Click the (+) icon to create new certificates. You need to create both a `Developer ID Installer` and a `Developer ID Application` certificate for distribution of Velopack apps outside the Mac App Store. ![apple certificate list](~/images/apple_certificate_list.png)
0. Open both certificates by clicking on them, press Download, and then double click the ".cer" file to install it to your local keychain.

### Setting up a NotaryTool profile
1. Create an app-specific password: https://support.apple.com/en-us/102654. You will only be shown this password once, so save or write it down somewhere.
0. Find your apple team ID: https://developer.apple.com/account#MembershipDetailsCard
0. Store your Apple account credentials to a new NotaryTool profile:
   ```sh
   xcrun notarytool store-credentials \
       --apple-id "yourapple@account.com" \
       --team-id "your-located-team-id" \
       --password "your-generated-app-specific-password" \
       "your-local-profile-name-here"
   ```

### Putting it all together
Now that you have your NotaryTool profile and code signing certificates installed, you can add the following parameters to your `pack` command:

```sh
vpk pack \
    ... 
    --signAppIdentity "Developer ID Application: Your Name" \
    --signInstallIdentity "Developer ID Installer: Your Name" \
    --notaryProfile "your-local-profile-name-here" \
```

When these parameters are specified and valid, Velopack will automatically code sign and notarize your application and installer packages.

### Automate signing in CI/CD (Github Actions)
It is also posible to store your certificates and notary credentials as Action Secrets and sign your code during CI builds.

1. Launch Keychain Access and open the "My Certificates" pane.
0. Select both certificates, right click and select "Export". Save as a p12 file and make note of the password. You can use the same password for both certificates.
0. Copy the contents of the certificate to clipboard as base64, example:
   ```sh
   base64 -i CERT.p12 | pbcopy
   ```
0. Create 7 [Github Secrets](https://docs.github.com/en/actions/security-guides/using-secrets-in-github-actions) for your Actions workflows
   - `BUILD_CERTIFICATE_BASE64` (b64 of your app cert)
   - `INSTALLER_CERTIFICATE_BASE64` (b64 of your installer cert)
   - `P12_PASSWORD` (password for the certificates)
   - `APPLE_ID` (your apple username)
   - `APPLE_PASSWORD` (your app-specific password from earlier)
   - `APPLE_TEAM` (your team id from earlier)
   - `KEYCHAIN_PASSWORD` (can be any random string, will be used to create a new keychain)

0. Add a step to your workflow which installs the certificates and keychain profile. Here is an example:
   ```yml
   name: App build & sign
   on: push
   jobs:
     build_with_signing:
       runs-on: macos-latest
       steps:
         - name: Checkout repository
           uses: actions/checkout@v4

         - name: Install Apple certificates and notary profile
           env:
             BUILD_CERTIFICATE_BASE64: ${{ secrets.BUILD_CERTIFICATE_BASE64 }}
             INSTALLER_CERTIFICATE_BASE64: ${{ secrets.INSTALLER_CERTIFICATE_BASE64 }}
             P12_PASSWORD: ${{ secrets.P12_PASSWORD }}
             APPLE_ID: ${{ secrets.APPLE_ID }}
             APPLE_PASSWORD: ${{ secrets.APPLE_PASSWORD }}
             APPLE_TEAM: ${{ secrets.APPLE_TEAM }}
             KEYCHAIN_PASSWORD: ${{ secrets.KEYCHAIN_PASSWORD }}
           run: |
             # create variables for file paths
             CERT_BUILD_PATH=$RUNNER_TEMP/build_certificate.p12
             CERT_INSTALLER_PATH=$RUNNER_TEMP/installer_certificate.p12
             KEYCHAIN_PATH=$RUNNER_TEMP/app-signing.keychain-db
   
             # import certificates from secrets
             echo -n "$BUILD_CERTIFICATE_BASE64" | base64 --decode -o $CERT_BUILD_PATH
             echo -n "$INSTALLER_CERTIFICATE_BASE64" | base64 --decode -o $CERT_INSTALLER_PATH
   
             # create temporary keychain
             security create-keychain -p "$KEYCHAIN_PASSWORD" $KEYCHAIN_PATH
             security set-keychain-settings -lut 21600 $KEYCHAIN_PATH
             security unlock-keychain -p "$KEYCHAIN_PASSWORD" $KEYCHAIN_PATH
   
             # import certificates to keychain
             security import $CERT_BUILD_PATH -P "$P12_PASSWORD" -A -t cert -f pkcs12 -k $KEYCHAIN_PATH
             security import $CERT_INSTALLER_PATH -P "$P12_PASSWORD" -A -t cert -f pkcs12 -k $KEYCHAIN_PATH
             security list-keychain -d user -s $KEYCHAIN_PATH
   
             # create notarytool profile
             xcrun notarytool store-credentials --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM" --password "$APPLE_PASSWORD" velopack-profile

         - name: Build app
           ...

         - name: Create Velopack Release
           run: |
             dotnet tool install -g vpk
             vpk ... --signAppIdentity "Developer ID Application: Your Name" --signInstallIdentity "Developer ID Installer: Your Name" --notaryProfile "velopack-profile"

         - name: Clean up keychain
           if: ${{ always() }}
           run: security delete-keychain $RUNNER_TEMP/app-signing.keychain-db
   ```