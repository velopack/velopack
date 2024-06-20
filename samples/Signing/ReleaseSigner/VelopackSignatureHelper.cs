using System;
using System.Security.Cryptography;
using System.Text;

namespace SigningSample;

internal static class VelopackSignatureHelper
{
	public const string SignatureAttribute = "Signature";

	public static bool VerifySignature(ReadOnlySpan<char> releaseContents, RSA key)
	{
		string expectedStart = $@"{{ ""Version"": 1, ""{SignatureAttribute}"": """;
		if (!releaseContents.StartsWith(expectedStart.AsSpan(), StringComparison.Ordinal))
			throw new CryptographicException("Release file was not signed");

		int hashEnd = releaseContents.Slice(expectedStart.Length).IndexOf('"');
		if (hashEnd < 0)
			throw new CryptographicException("Release file was not signed");

		string signature = releaseContents.Slice(expectedStart.Length, hashEnd).ToString();
		string jsonWithoutHash = string.Concat("{", releaseContents.Slice(expectedStart.Length + hashEnd + 3).ToString());

		byte[] data = GetCanonicalDataForHashing(jsonWithoutHash);
		return key.VerifyData(data, Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
	}

	public static string AddSignature(string releaseContents, RSA key)
	{
		if (releaseContents.Contains(SignatureAttribute))
			throw new InvalidOperationException("Release file already signed");

		byte[] data = GetCanonicalDataForHashing(releaseContents);
		byte[] signed = key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

		string signatureString = Convert.ToBase64String(signed);

		string jsonStart = $" \"Version\": 1, \"{SignatureAttribute}\": \"{signatureString}\",\n";
		return releaseContents.Insert(1, jsonStart);
	}

	/// <summary>
	/// Convert json to "canonical form" so that data
	/// is the same even if newline char changes and return a byte array that can be used for hashing
	/// </summary>
	/// <remarks>
	/// We could strip leading whitespace or other nonsignificant whitespace, or similar which does not affect the JSON data.
	/// But the file should not be modified anyway, and we don't want to risk any flaw that would allow an attacker
	/// to change the file without the signature detecting it.
	/// </remarks>
	static byte[] GetCanonicalDataForHashing(string releaseContents)
	{
		// 1. Change line endings from windows to Linux
		releaseContents = releaseContents.Replace("\r\n", "\n");

		return Encoding.UTF8.GetBytes(releaseContents);
	}
}
