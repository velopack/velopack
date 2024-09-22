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

using System;
using System.Runtime.InteropServices;

namespace NCode.ReparsePoints.Win32
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    internal struct ReparseHeader
    {
        public uint ReparseTag;
        public ushort ReparseDataLength;

        public ushort Reserved;
        // next in memory:
        // ReparseData
        // SubstituteName
        // PrintName
    }

    internal interface IReparseData
    {
        ushort SubstituteNameOffset { get; }
        ushort SubstituteNameLength { get; }
        ushort PrintNameOffset { get; }
        ushort PrintNameLength { get; }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    internal struct JunctionData : IReparseData
    {
        public ushort SubstituteNameOffset { get; set; }
        public ushort SubstituteNameLength { get; set; }
        public ushort PrintNameOffset { get; set; }
        public ushort PrintNameLength { get; set; }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    internal struct SymbolicData : IReparseData
    {
        public ushort SubstituteNameOffset { get; set; }
        public ushort SubstituteNameLength { get; set; }
        public ushort PrintNameOffset { get; set; }
        public ushort PrintNameLength { get; set; }
        public uint Flags { get; set; }
    }
}