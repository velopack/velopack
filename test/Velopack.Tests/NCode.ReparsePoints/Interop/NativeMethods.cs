#region Copyright Preamble

//
//    Copyright � 2015 NCode Group
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//

#endregion

#pragma warning disable SYSLIB0004

using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;

namespace NCode.ReparsePoints.Win32
{
    [SecurityCritical]
    [SuppressUnmanagedCodeSecurity]
    internal static class NativeMethods
    {
        private const string Kernel32 = "kernel32.dll";

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFindHandle FindFirstFile(
            [In]
            string lpFileName,
            [Out]
            out Win32FindData lpFindFileData);

        [DllImport(Kernel32, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindClose(
            [In]
            IntPtr hFindFile);

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            [In]
            SafeFileHandle hDevice,
            [In]
            uint dwIoControlCode,
            [In]
            SafeLocalAllocHandle lpInBuffer,
            [In]
            int nInBufferSize,
            [In]
            SafeLocalAllocHandle lpOutBuffer,
            [In]
            int nOutBufferSize,
            [Out]
            out int lpBytesReturned,
            [In]
            IntPtr lpOverlapped);

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(
            [In]
            string lpFileName,
            [In]
            AccessRights dwDesiredAccess,
            [In]
            FileShareMode dwShareMode,
            [In]
            IntPtr lpSecurityAttributes,
            [In]
            FileCreationDisposition dwCreationDisposition,
            [In]
            FileAttributeFlags dwFlagsAndAttributes,
            [In]
            IntPtr hTemplateFile);

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateHardLink(
            [In]
            string lpFileName,
            [In]
            string lpExistingFileName,
            [In]
            IntPtr lpSecurityAttributes);

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool CreateSymbolicLink(
            [In]
            string lpSymlinkFileName,
            [In]
            string lpTargetFileName,
            [In]
            SymbolicLinkFlag dwFlags);

        [DllImport(Kernel32, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern SafeLocalAllocHandle LocalAlloc(
            [In]
            AllocFlags flags,
            [In]
            IntPtr cb);

        [DllImport(Kernel32, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        public static extern IntPtr LocalFree(
            [In]
            IntPtr handle);
    }
}