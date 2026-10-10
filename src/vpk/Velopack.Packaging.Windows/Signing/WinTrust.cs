#nullable enable
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// Minimal WinVerifyTrust wrapper (no UI, no revocation checks, no network retrieval) for verifying signatures on Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WinTrust
{
    public const uint TRUST_E_NOSIGNATURE = 0x800B0100;
    public const uint TRUST_E_SUBJECT_FORM_UNKNOWN = 0x800B0003;
    public const uint TRUST_E_BAD_DIGEST = 0x80096010;
    public const uint CERT_E_UNTRUSTEDROOT = 0x800B0109;
    public const uint CERT_E_CHAINING = 0x800B010A;

    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    /// <summary>
    /// True if <paramref name="result"/> means the file has an intact signature, whether or not its root is trusted by
    /// this machine (Private Trust / test certificate profiles chain to roots Windows does not trust for code signing).
    /// </summary>
    public static bool IsIntactSignature(uint result) => result is 0 or CERT_E_UNTRUSTEDROOT or CERT_E_CHAINING;

    /// <summary>Returns the WinVerifyTrust HRESULT for <paramref name="path"/> (0 = signed and trusted).</summary>
    public static uint Verify(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO {
            cbStruct = (uint) Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path,
        };

        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA {
                cbStruct = (uint) Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2, // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1, // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 1, // WTD_STATEACTION_VERIFY
                dwProvFlags = 0x00001000, // WTD_CACHE_ONLY_URL_RETRIEVAL
            };

            var action = WintrustActionGenericVerifyV2;
            uint result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            data.dwStateAction = 2; // WTD_STATEACTION_CLOSE
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            return result;
        } finally {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

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
}
