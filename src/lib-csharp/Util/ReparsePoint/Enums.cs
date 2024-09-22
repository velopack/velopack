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

namespace NCode.ReparsePoints
{
    /// <summary>
    /// Represents the type of reparse point such as a hard link, junction (aka
    /// soft link), or symbolic link.
    /// </summary>
    [Serializable]
    internal enum LinkType
    {
        /// <summary>
        /// Represents an unknown reparse point type.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Represents a file <c>hard link</c>.
        /// </summary>
        /// <remarks>
        /// Technically not a reparse point.
        /// </remarks>
        HardLink,

        /// <summary>
        /// Represents a directory <c>junction</c> (aka soft link).
        /// </summary>
        Junction,

        /// <summary>
        /// Represents a <c>symbolic link</c> to either a file or folder.
        /// </summary>
        /// <remarks>
        /// In order to create symbolic links, the current user must either be an
        /// administrator running with elevated privileges or a non-admin user that
        /// has the SeCreateSymbolicLinkPrivilege right in local security policy.
        /// </remarks>
        Symbolic
    }
}