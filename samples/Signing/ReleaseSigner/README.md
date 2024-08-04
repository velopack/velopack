# Release Signer
This is a small helper program to sign the `release.win.json` file created by Velopack.

## Usage

```
> .\ReleaseSigner.exe --help

Usage: [command] [-h|--help] [--version]

Commands:
  export keyvault    Exports the public RSA key from Azure Key Vault to a specified file.
  generate rsa
  sign keyvault      Signs the release file using keys from keyvault
  sign rsa
  verify keyvault    Verifies the signature of a release file using a key from Azure Key Vault.
  verify rsa

```

##  Examples

### Local RSA Keys
```

# To generate new keys (public and private)
.\ReleaseSigner.exe generate rsa 

# To sign the release.win.json file run
.\ReleaseSigner.exe sign rsa release.win.json

# To verify the signed file run
.\ReleaseSigner.exe verify rsa release.win.json

```

### Azure keyvault
```

# To export public key to local file run
.\ReleaseSigner.exe --key-vault-uri https://KEYVAULT.vault.azure.net/ --key-vault-key VelopackReleases

# To sign the release.win.json file run
.\ReleaseSigner.exe sign keyvault release.win.json --key-vault-uri https://KEYVAULT.vault.azure.net/ --key-vault-key VelopackReleases

# To verify the signed file run
.\ReleaseSigner.exe verify keyvault release.win.json --key-vault-uri https://KEYVAULT.vault.azure.net/ --key-vault-key VelopackReleases

```

## Key rotation

1. Create a new key
2. Export public part of keys and make sure that the application supports **both old and new key**
3. Ensure new app has rolled out to all users
4. Make sure installers are updated and start using the new key 
