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

using System.IO;

namespace NCode.ReparsePoints
{
    /// <summary>
    /// Contains the information about a reparse point.
    /// </summary>
    internal struct ReparseLink
    {
        /// <summary>
        /// Contains the <see cref="FileAttributes"/> of a reparse point.
        /// </summary>
        public FileAttributes Attributes { get; set; }

        /// <summary>
        /// Contains the <see cref="LinkType"/> of a reparse point.
        /// </summary>
        public LinkType Type { get; set; }

        /// <summary>
        /// Contains the target of a reparse point.
        /// </summary>
        /// <remarks>
        /// The target for a hard link cannot be determined so this member will
        /// always be <c>null</c> for hard links.
        /// </remarks>
        public string Target { get; set; }
    }
}