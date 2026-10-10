#nullable enable
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace Velopack.Packaging.Tests.Signing;

/// <summary>
/// Asks WinVerifyTrust (not .NET) which timestamp counter-signers it found on a file's primary signature, so tests can prove
/// Windows itself recognizes the embedded RFC3161 timestamp regardless of whether the signing chain is trusted.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WinTrustTimestampProbe
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    /// <summary>Returns the signing certificate of each counter-signer (timestamp) Windows parsed on the primary signer.</summary>
    public static List<X509Certificate2> GetTimestampSigners(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO {
            cbStruct = (uint) Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path,
        };

        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        var action = WintrustActionGenericVerifyV2;
        var data = new WINTRUST_DATA {
            cbStruct = (uint) Marshal.SizeOf<WINTRUST_DATA>(),
            dwUIChoice = 2, // WTD_UI_NONE
            fdwRevocationChecks = 0, // WTD_REVOKE_NONE
            dwUnionChoice = 1, // WTD_CHOICE_FILE
            pFile = pFile,
            dwStateAction = 1, // WTD_STATEACTION_VERIFY
            dwProvFlags = 0x00001000, // WTD_CACHE_ONLY_URL_RETRIEVAL
        };
        try {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);

            var result = new List<X509Certificate2>();
            IntPtr provData = WTHelperProvDataFromStateData(data.hWVTStateData);
            if (provData == IntPtr.Zero) return result;
            IntPtr signerPtr = WTHelperGetProvSignerFromChain(provData, 0, false, 0);
            if (signerPtr == IntPtr.Zero) return result;

            var signer = Marshal.PtrToStructure<CRYPT_PROVIDER_SGNR>(signerPtr);
            int counterSignerSize = Marshal.SizeOf<CRYPT_PROVIDER_SGNR>();
            for (int i = 0; i < signer.csCounterSigners; i++) {
                var counterSigner = Marshal.PtrToStructure<CRYPT_PROVIDER_SGNR>(signer.pasCounterSigners + i * counterSignerSize);
                if (counterSigner.csCertChain == 0) continue;
                var cert = Marshal.PtrToStructure<CRYPT_PROVIDER_CERT>(counterSigner.pasCertChain);
                result.Add(new X509Certificate2(cert.pCert));
            }

            return result;
        } finally {
            data.dwStateAction = 2; // WTD_STATEACTION_CLOSE
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [DllImport("wintrust.dll")]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr hStateData);

    [DllImport("wintrust.dll")]
    private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr pProvData, uint idxSigner, bool fCounterSigner, uint idxCounterSigner);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CRYPT_PROVIDER_SGNR
    {
        public uint cbStruct;
        public uint sftVerifyAsOfLow; // FILETIME
        public uint sftVerifyAsOfHigh;
        public uint csCertChain;
        public IntPtr pasCertChain;
        public uint dwSignerType;
        public IntPtr psSigner;
        public uint dwError;
        public uint csCounterSigners;
        public IntPtr pasCounterSigners;
        public IntPtr pChainContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CRYPT_PROVIDER_CERT
    {
        public uint cbStruct;
        public IntPtr pCert;
    }
}
