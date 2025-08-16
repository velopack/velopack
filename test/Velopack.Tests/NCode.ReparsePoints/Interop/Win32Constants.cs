#region Copyright Preamble

//
//    Copyright © 2015 NCode Group
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

namespace NCode.ReparsePoints.Win32
{
    internal static class Win32Constants
    {
        public const int MaxPath = 260;

        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_NOT_A_REPARSE_POINT = 4390;

        public const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
        public const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;

        public const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;
        public const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;

        public const string NonInterpretedPathPrefix = "\\??\\";

        public static readonly string[] DosDevicePrefixes = {
            "\\??\\",
            "\\DosDevices\\",
            "\\Global??\\"
        };
    }
}