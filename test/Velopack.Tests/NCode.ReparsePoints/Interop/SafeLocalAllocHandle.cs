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

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NCode.ReparsePoints.Win32
{
    [SecurityCritical]
    internal class SafeLocalAllocHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        #region Static Members

        public static SafeLocalAllocHandle InvalidHandle {
            get { return new SafeLocalAllocHandle(IntPtr.Zero); }
        }

        public static SafeLocalAllocHandle Allocate(int cb)
        {
            return Allocate(new IntPtr(cb));
        }

        public static SafeLocalAllocHandle Allocate(IntPtr cb)
        {
            return NativeMethods.LocalAlloc(AllocFlags.Fixed, cb);
        }

        #endregion

        protected SafeLocalAllocHandle()
            : base(true)
        {
            // do not delete this ctor
            // it is required for pinvoke
        }

        public SafeLocalAllocHandle(IntPtr handle)
            : base(true)
        {
            SetHandle(handle);
        }

        public virtual int Write(int position, byte[] buffer, int offset, int count)
        {
            Marshal.Copy(buffer, offset, handle + position, count);
            return count;
        }

        public virtual int Write<T>(int position, T value) where T : notnull
        {
            var count = Marshal.SizeOf(value);
            Marshal.StructureToPtr(value, handle + position, false);
            return count;
        }

        public virtual T Read<
#if NET7_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
#endif
            T>(
            int position) where T : notnull
        {
            var value = Marshal.PtrToStructure<T>(handle + position);
            return value!;
        }

        public virtual void Read(int position, byte[] buffer, int offset, int count)
        {
            Marshal.Copy(handle + position, buffer, offset, count);
        }

        public virtual string ReadString(int position, int byteCount, Encoding encoding)
        {
            var bytes = new byte[byteCount];
            Read(position, bytes, 0, byteCount);
            var str = encoding.GetString(bytes);
            return str;
        }

        [SecurityCritical]
        protected override bool ReleaseHandle()
        {
            return NativeMethods.LocalFree(handle) == IntPtr.Zero;
        }
    }
}