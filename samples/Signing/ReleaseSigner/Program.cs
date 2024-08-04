using System.Security.Cryptography;
using System.Text;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using ConsoleAppFramework;
using SigningSample;

var app = ConsoleApp.Create();

app.Add("generate rsa", ([Argument] int keySize = 2048) =>
{
	var rsa = RSA.Create(keySize);

	File.WriteAllText("rsa.pem", rsa.ExportRSAPrivateKeyPem());
	File.WriteAllText("rsa.public.pem", rsa.ExportRSAPublicKeyPem());
	// NET FRAMEWORK only support XML, and web installer runs on .NET FRAMEWORK
	File.WriteAllText("rsa.public.xml", rsa.ToXmlString(includePrivateParameters: false));
});

app.Add("export keyvault", ExportKeyVaultKey);

app.Add("sign rsa", ([Argument] string releaseFile, string rsaFile = "rsa.pem") =>
{
	Console.WriteLine("Create RSA");
	using var rsa = RSACryptoServiceProvider.Create();
	Console.WriteLine("Loading RSA");
	rsa.ImportFromPem(File.ReadAllText(rsaFile));

	AddSignature(releaseFile, rsa);
});

app.Add("sign keyvault", AddSignatureUsingKeyVault);

app.Add("verify rsa", ([Argument] string releaseFile, string rsaFile = "rsa.public.pem") =>
{
	Console.WriteLine("Create RSA");
	var rsa = RSACryptoServiceProvider.Create();
	Console.WriteLine("Loading RSA");
	rsa.ImportFromPem(File.ReadAllText(rsaFile));

	VerifySignature(releaseFile, rsa);
});

app.Add("verify keyvault", VerifySignatureUsingKeyVault);

app.Run(args);
return;

/// <summary>
/// Signs the release file using keys from keyvault
/// </summary>
/// <param name="releaseFile">path to releases.win.json</param>
/// <param name="keyVaultUri">KeyVault uri</param>
/// <param name="keyVaultKey">name of key in KeyVault</param>
static void AddSignatureUsingKeyVault([Argument] string releaseFile, string keyVaultUri, string keyVaultKey)
{
	using RSA key = GetKeyVaultKey(keyVaultUri, keyVaultKey);

	AddSignature(releaseFile, key);
}

static void AddSignature(string releaseFile, RSA key)
{
	// Read release file
	string releaseContents = File.ReadAllText(releaseFile, Encoding.UTF8);

	releaseContents = VelopackSignatureHelper.AddSignature(releaseContents, key);

	File.WriteAllText(releaseFile, releaseContents, Encoding.UTF8);
    Console.WriteLine("Signature added");
}

/// <summary>
/// Verifies the signature of a release file using a key from Azure Key Vault.
/// </summary>
/// <param name="releaseFile">The path to the release file.</param>
/// <param name="keyVaultUri">The URI of the Azure Key Vault.</param>
/// <param name="keyVaultKey">The name of the key in Azure Key Vault.</param>
static void VerifySignatureUsingKeyVault([Argument] string releaseFile, string keyVaultUri, string keyVaultKey)
{
	using RSA key = GetKeyVaultKey(keyVaultUri, keyVaultKey);

	VerifySignature(releaseFile, key);
}

static bool VerifySignature(string releaseFile, RSA key)
{
	// Read release file
	var releaseContents = File.ReadAllText(releaseFile, Encoding.UTF8).AsSpan();

	if (VelopackSignatureHelper.VerifySignature(releaseContents, key))
	{
		Console.WriteLine("Validation OK");
		return true;
	}
	else
	{
		Console.WriteLine("Validation FAILED");
		return false;
	}
}

/// <summary>
/// Exports the public RSA key from Azure Key Vault to a specified file.
/// </summary>
/// <param name="keyVaultUri">The URI of the Azure Key Vault.</param>
/// <param name="keyVaultKey">The name of the key in Azure Key Vault.</param>
/// <param name="rsaFile">The file path where the exported RSA public key will be saved. Defaults to "rsa.public.pem".</param>
static void ExportKeyVaultKey(string keyVaultUri, string keyVaultKey, string rsaFile = "rsa.public.pem")
{
	using RSA rsa = GetKeyVaultKey(keyVaultUri, keyVaultKey);

	switch (rsa)
	{
		case RSAKeyVault rsaKeyVault:
			File.WriteAllText(rsaFile, rsaKeyVault.ExportRSAPublicKeyPem());
			File.WriteAllText("rsa.public.xml", rsaKeyVault.ToXmlString(includePrivateParameters: false));
			break;
		default:
			throw new InvalidOperationException("Key is not RSA");
	}
}

static RSAKeyVault GetKeyVaultKey(string keyVaultUri, string keyVaultKey)
{
	KeyClient keyVault = new KeyClient(new Uri(keyVaultUri), new DefaultAzureCredential());
	CryptographyClient client = keyVault.GetCryptographyClient(keyVaultKey);
	RSAKeyVault rsa = client.CreateRSA();
	return rsa;
}

