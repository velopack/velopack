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

namespace NCode.ReparsePoints.Win32
{
    /// <remarks>
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa374896(v=vs.85).aspx
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa374892(v=vs.85).aspx
    /// </remarks>
    [Flags]
    [Serializable]
    internal enum AccessRights : uint
    {
        None = 0x0,

        /// <summary>
        /// The MAXIMUM_ALLOWED access type is generally used with the AccessCheck(…)
        /// function to determine whether a security descriptor grants a specified
        /// set of access rights to the client identified by an access token.
        /// Typically, server applications use this function to check access to a
        /// private object. Note that MAXIMUM_ALLOWED cannot be used in an ACE (see
        /// access control entries).
        /// </summary>
        /// <remarks>MAXIMUM_ALLOWED</remarks>
        MaximumAllowed = 0x02000000,

        #region Standard Access Rights

        /// <summary>
        /// The right to delete the object.
        /// </summary>
        /// <remarks>DELETE</remarks>
        Delete = 0x00010000,

        /// <summary>
        /// The right to read the information in the object's security descriptor,
        /// not including the information in the system access control list (SACL).
        /// </summary>
        /// <remarks>READ_CONTROL</remarks>
        ReadControl = 0x00020000,

        /// <summary>
        /// The right to modify the discretionary access control list (DACL) in the
        /// object's security descriptor.
        /// </summary>
        /// <remarks>WRITE_DAC</remarks>
        WriteDac = 0x00040000,

        /// <summary>
        /// The right to change the owner in the object's security descriptor.
        /// </summary>
        /// <remarks>WRITE_OWNER</remarks>
        WriteOwner = 0x00080000,

        /// <summary>
        /// The right to use the object for synchronization. This enables a thread
        /// to wait until the object is in the signaled state. Some object types do not support this access right.
        /// </summary>
        /// <remarks>SYNCHRONIZE</remarks>
        Synchronize = 0x00100000,

        /// <summary>
        /// Currently defined to equal READ_CONTROL.
        /// </summary>
        /// <remarks>STANDARD_RIGHTS_READ</remarks>
        StandardRightsRead = 0x00020000,

        /// <summary>
        /// Currently defined to equal READ_CONTROL.
        /// </summary>
        /// <remarks>STANDARD_RIGHTS_WRITE</remarks>
        StandardRightsWrite = 0x00020000,

        /// <summary>
        /// Currently defined to equal READ_CONTROL.
        /// </summary>
        /// <remarks>STANDARD_RIGHTS_EXECUTE</remarks>
        StandardRightsExecute = 0x00020000,

        /// <summary>
        /// Combines DELETE, READ_CONTROL, WRITE_DAC, and WRITE_OWNER access.
        /// </summary>
        /// <remarks>STANDARD_RIGHTS_REQUIRED</remarks>
        StandardRightsRequired = 0x000F0000,

        /// <summary>
        /// Combines DELETE, READ_CONTROL, WRITE_DAC, WRITE_OWNER, and SYNCHRONIZE access.
        /// </summary>
        /// <remarks>STANDARD_RIGHTS_ALL</remarks>
        StandardRightsAll = 0x001F0000,

        #endregion

        #region Generic Access Rights

        /// <summary>
        /// Execute access.
        /// </summary>
        /// <remarks>GENERIC_EXECUTE</remarks>
        GenericExecute = 0x20000000,

        /// <summary>
        /// Write access.
        /// </summary>
        /// <remarks>GENERIC_WRITE</remarks>
        GenericWrite = 0x40000000,

        /// <summary>
        /// Read access.
        /// </summary>
        /// <remarks>GENERIC_READ</remarks>
        GenericRead = 0x80000000,

        /// <summary>
        /// All possible access rights.
        /// </summary>
        /// <remarks>GENERIC_ALL</remarks>
        GenericAll = 0x10000000,

        #endregion

        #region File Security and Access Rights

        // http://msdn.microsoft.com/en-us/library/windows/desktop/aa364399(v=vs.85).aspx

        /// <summary>
        /// For a file object, the right to read the corresponding file data. For
        /// a directory object, the right to read the corresponding directory data.
        /// </summary>
        /// <remarks>FILE_READ_DATA</remarks>
        FileReadData = 0x0001,

        /// <summary>
        /// For a directory, the right to list the contents of the directory.
        /// </summary>
        /// <remarks>FILE_LIST_DIRECTORY</remarks>
        FileListDirectory = 0x0001,

        /// <summary>
        /// For a file object, the right to write data to the file. For a directory
        /// object, the right to create a file in the directory (FILE_ADD_FILE).
        /// </summary>
        /// <remarks>FILE_WRITE_DATA</remarks>
        FileWriteData = 0x0002,

        /// <summary>
        /// For a directory, the right to create a file in the directory.
        /// </summary>
        /// <remarks>FILE_ADD_FILE</remarks>
        FileAddFile = 0x0002,

        /// <summary>
        /// For a file object, the right to append data to the file. (For local
        /// files, write operations will not overwrite existing data if this flag
        /// is specified without FILE_WRITE_DATA.) For a directory object, the
        /// right to create a subdirectory (FILE_ADD_SUBDIRECTORY).
        /// </summary>
        /// <remarks>FILE_APPEND_DATA</remarks>
        FileAppendData = 0x0004,

        /// <summary>
        /// For a directory, the right to create a subdirectory.
        /// </summary>
        /// <remarks>FILE_ADD_SUBDIRECTORY</remarks>
        FileAddSubdirectory = 0x0004,

        /// <summary>
        /// For a named pipe, the right to create a pipe.
        /// </summary>
        /// <remarks>FILE_CREATE_PIPE_INSTANCE</remarks>
        FileCreatePipeInstance = 0x0004,

        /// <summary>
        /// The right to read extended file attributes.
        /// </summary>
        /// <remarks>FILE_READ_EA</remarks>
        FileReadExtendedAttributes = 0x0008,

        /// <summary>
        /// The right to write extended file attributes.
        /// </summary>
        /// <remarks>FILE_WRITE_EA</remarks>
        FileWriteExtendedAttributes = 0x0010,

        /// <summary>
        /// For a native code file, the right to execute the file. This access
        /// right given to scripts may cause the script to be executable,
        /// depending on the script interpreter.
        /// </summary>
        /// <remarks>FILE_EXECUTE</remarks>
        FileExecute = 0x0020,

        /// <summary>
        /// For a directory, the right to traverse the directory. By default, users
        /// are assigned the BYPASS_TRAVERSE_CHECKING privilege, which ignores the
        /// FILE_TRAVERSE access right. See the remarks in File Security and Access
        /// Rights for more information.
        /// </summary>
        /// <remarks>FILE_TRAVERSE</remarks>
        FileTraverse = 0x0020,

        /// <summary>
        /// For a directory, the right to delete a directory and all the files it
        /// contains, including read-only files.
        /// </summary>
        /// <remarks>FILE_DELETE_CHILD</remarks>
        FileDeleteChild = 0x0040,

        /// <summary>
        /// The right to read file attributes.
        /// </summary>
        /// <remarks>FILE_READ_ATTRIBUTES</remarks>
        FileReadAttributes = 0x0080,

        /// <summary>
        /// The right to write file attributes.
        /// </summary>
        /// <remarks>FILE_WRITE_ATTRIBUTES</remarks>
        FileWriteAttributes = 0x0100,

        #endregion

        #region Thread Security and Access Rights

        // http://msdn.microsoft.com/en-us/library/windows/desktop/ms686769(v=vs.85).aspx

        /// <summary>
        /// Required to terminate a thread using TerminateThread.
        /// </summary>
        /// <remarks>THREAD_TERMINATE</remarks>
        ThreadTerminate = 0x0001,

        /// <summary>
        /// Required to suspend or resume a thread (see SuspendThread and ResumeThread).
        /// </summary>
        /// <remarks>THREAD_SUSPEND_RESUME</remarks>
        ThreadSuspendResume = 0x0002,

        /// <summary>
        /// Required to read the context of a thread using GetThreadContext.
        /// </summary>
        /// <remarks>THREAD_GET_CONTEXT</remarks>
        ThreadGetContext = 0x0008,

        /// <summary>
        /// Required to write the context of a thread using SetThreadContext.
        /// </summary>
        /// <remarks>THREAD_SET_CONTEXT</remarks>
        ThreadSetContext = 0x0010,

        /// <summary>
        /// Required to set certain information in the thread object.
        /// </summary>
        /// <remarks>THREAD_SET_INFORMATION</remarks>
        ThreadSetInformation = 0x0020,

        /// <summary>
        /// Required to read certain information from the thread object, such as the exit code (see GetExitCodeThread).
        /// </summary>
        /// <remarks>THREAD_QUERY_INFORMATION</remarks>
        ThreadQueryInformation = 0x0040,

        /// <summary>
        /// Required to set the impersonation token for a thread using SetThreadToken.
        /// </summary>
        /// <remarks>THREAD_SET_THREAD_TOKEN</remarks>
        ThreadSetThreadToken = 0x0080,

        /// <summary>
        /// Required to use a thread's security information directly without calling it by using a communication mechanism that provides impersonation services.
        /// </summary>
        /// <remarks>THREAD_IMPERSONATE</remarks>
        ThreadImpersonate = 0x0100,

        /// <summary>
        /// Required for a server thread that impersonates a client.
        /// </summary>
        /// <remarks>THREAD_DIRECT_IMPERSONATION</remarks>
        ThreadDirectImpersonation = 0x0200,

        /// <summary>
        /// All possible access rights for a thread object.
        /// </summary>
        /// <remarks>THREAD_ALL_ACCESS</remarks>
        ThreadAllAccess = (StandardRightsRequired | Synchronize | 0x3FF),

        #endregion

        #region Process Security and Access Rights

        // http://msdn.microsoft.com/en-us/library/windows/desktop/ms684880(v=vs.85).aspx

        /// <summary>
        /// Required to terminate a process using TerminateProcess.
        /// </summary>
        /// <remarks>PROCESS_TERMINATE</remarks>
        ProcessTerminate = 0x00000001,

        /// <summary>
        /// Required to create a thread.
        /// </summary>
        /// <remarks>PROCESS_CREATE_THREAD</remarks>
        ProcessCreateThread = 0x00000002,

        /// <summary>
        /// Required to perform an operation on the address space of a process (see VirtualProtectEx and WriteProcessMemory).
        /// </summary>
        /// <remarks>PROCESS_VM_OPERATION</remarks>
        ProcessVmOperation = 0x00000008,

        /// <summary>
        /// Required to read memory in a process using ReadProcessMemory.
        /// </summary>
        /// <remarks>PROCESS_VM_READ</remarks>
        ProcessVmRead = 0x00000010,

        /// <summary>
        /// Required to write to memory in a process using WriteProcessMemory.
        /// </summary>
        /// <remarks>PROCESS_VM_WRITE</remarks>
        ProcessVmWrite = 0x00000020,

        /// <summary>
        /// Required to duplicate a handle using DuplicateHandle.
        /// </summary>
        /// <remarks>PROCESS_DUP_HANDLE</remarks>
        ProcessDupHandle = 0x00000040,

        /// <summary>
        /// Required to create a process.
        /// </summary>
        /// <remarks>PROCESS_CREATE_PROCESS</remarks>
        ProcessCreateProcess = 0x00000080,

        /// <summary>
        /// Required to set memory limits using SetProcessWorkingSetSize.
        /// </summary>
        /// <remarks>PROCESS_SET_QUOTA</remarks>
        ProcessSetQuota = 0x00000100,

        /// <summary>
        /// Required to set certain information about a process, such as its priority class (see SetPriorityClass).
        /// </summary>
        /// <remarks>PROCESS_SET_INFORMATION</remarks>
        ProcessSetInformation = 0x00000200,

        /// <summary>
        /// Required to retrieve certain information about a process, such as its token, exit code, and priority class (see OpenProcessToken).
        /// </summary>
        /// <remarks>PROCESS_QUERY_INFORMATION</remarks>
        ProcessQueryInformation = 0x00000400,

        /// <summary>
        /// All possible access rights for a process object.
        /// </summary>
        /// <remarks>PROCESS_ALL_ACCESS</remarks>
        ProcessAllAccess = (StandardRightsRequired | Synchronize | 0xFFF),

        #endregion

        #region Token Security and Access Rights

        // http://msdn.microsoft.com/en-us/library/windows/desktop/aa374905(v=vs.85).aspx

        /// <summary>
        /// Required to attach a primary token to a process. The SE_ASSIGNPRIMARYTOKEN_NAME privilege is also required to accomplish this task.
        /// </summary>
        /// <remarks>TOKEN_ASSIGN_PRIMARY</remarks>
        TokenAssignPrimary = 0x0001,

        /// <summary>
        /// Required to duplicate an access token.
        /// </summary>
        /// <remarks>TOKEN_DUPLICATE</remarks>
        TokenDuplicate = 0x0002,

        /// <summary>
        /// Required to attach an impersonation access token to a process.
        /// </summary>
        /// <remarks>TOKEN_IMPERSONATE</remarks>
        TokenImpersonate = 0x0004,

        /// <summary>
        /// Required to query an access token.
        /// </summary>
        /// <remarks>TOKEN_QUERY</remarks>
        TokenQuery = 0x0008,

        /// <summary>
        /// Required to query the source of an access token.
        /// </summary>
        /// <remarks>TOKEN_QUERY_SOURCE</remarks>
        TokenQuerySource = 0x0010,

        /// <summary>
        /// Required to enable or disable the privileges in an access token.
        /// </summary>
        /// <remarks>TOKEN_ADJUST_PRIVILEGES</remarks>
        TokenAdjustPrivileges = 0x0020,

        /// <summary>
        /// Required to adjust the attributes of the groups in an access token.
        /// </summary>
        /// <remarks>TOKEN_ADJUST_GROUPS</remarks>
        TokenAdjustGroups = 0x0040,

        /// <summary>
        /// Required to change the default owner, primary group, or DACL of an access token.
        /// </summary>
        /// <remarks>TOKEN_ADJUST_DEFAULT</remarks>
        TokenAdjustDefault = 0x0080,

        /// <summary>
        /// Required to adjust the session ID of an access token. The SE_TCB_NAME privilege is required.
        /// </summary>
        /// <remarks>TOKEN_ADJUST_SESSIONID</remarks>
        TokenAdjustSessionid = 0x0100,

        /// <summary>
        /// Combines STANDARD_RIGHTS_READ and TOKEN_QUERY.
        /// </summary>
        /// <remarks>TOKEN_READ</remarks>
        TokenRead = (StandardRightsRead | TokenQuery),

        /// <summary>
        /// Combines STANDARD_RIGHTS_WRITE, TOKEN_ADJUST_PRIVILEGES, TOKEN_ADJUST_GROUPS, and TOKEN_ADJUST_DEFAULT.
        /// </summary>
        /// <remarks>TOKEN_WRITE</remarks>
        TokenWrite = (StandardRightsWrite | TokenAdjustPrivileges | TokenAdjustGroups | TokenAdjustDefault),

        /// <summary>
        /// Combines STANDARD_RIGHTS_EXECUTE and TOKEN_IMPERSONATE.
        /// </summary>
        /// <remarks>TOKEN_EXECUTE</remarks>
        TokenExecute = (StandardRightsExecute | TokenImpersonate),

        /// <summary>
        /// Combines all possible access rights for a token.
        /// </summary>
        /// <remarks>TOKEN_ALL_ACCESS</remarks>
        TokenAllAccess = (StandardRightsRequired | TokenAssignPrimary | TokenDuplicate | TokenImpersonate | TokenQuery | TokenQuerySource |
                          TokenAdjustPrivileges | TokenAdjustGroups | TokenAdjustDefault | TokenAdjustSessionid),

        #endregion
    }

    [Flags]
    [Serializable]
    internal enum FileShareMode
    {
        /// <summary>
        /// Prevents other processes from opening a file or device if they request
        /// delete, read, or write access.
        /// </summary>
        None = 0x00000000,

        /// <summary>
        /// Enables subsequent open operations on a file or device to request read
        /// access. Otherwise, other processes cannot open the file or device if
        /// they request read access. If this flag is not specified, but the file
        /// or device has been opened for read access, the function fails.
        /// </summary>
        /// <remarks>FILE_SHARE_READ</remarks>
        FileShareRead = 0x00000001,

        /// <summary>
        /// Enables subsequent open operations on a file or device to request write
        /// access. Otherwise, other processes cannot open the file or device if
        /// they request write access. If this flag is not specified, but the file
        /// or device has been opened for write access or has a file mapping with
        /// write access, the function fails.
        /// </summary>
        /// <remarks>FILE_SHARE_WRITE</remarks>
        FileShareWrite = 0x00000002,

        /// <summary>
        /// Enables subsequent open operations on a file or device to request delete
        /// access. Otherwise, other processes cannot open the file or device if
        /// they request delete access. If this flag is not specified, but the file
        /// or device has been opened for delete access, the function fails. Note
        /// Delete access allows both delete and rename operations.
        /// </summary>
        /// <remarks>FILE_SHARE_DELETE</remarks>
        FileShareDelete = 0x00000004,
    }

    [Serializable]
    internal enum FileCreationDisposition
    {
        /// <summary>
        /// Creates a new file, only if it does not already exist. If the specified
        /// file exists, the function fails and the last-error code is set to
        /// ERROR_FILE_EXISTS (80). If the specified file does not exist and is a
        /// valid path to a writable location, a new file is created.
        /// </summary>
        /// <remarks>CREATE_NEW</remarks>
        CreateNew = 1,

        /// <summary>
        /// Creates a new file, always. If the specified file exists and is writable,
        /// the function overwrites the file, the function succeeds, and last-error
        /// code is set to ERROR_ALREADY_EXISTS (183). If the specified file does
        /// not exist and is a valid path, a new file is created, the function
        /// succeeds, and the last-error code is set to zero. For more information,
        /// see the Remarks section of this topic.
        /// </summary>
        /// <remarks>CREATE_ALWAYS</remarks>
        CreateAlways = 2,

        /// <summary>
        /// Opens a file or device, only if it exists. If the specified file or
        /// device does not exist, the function fails and the last-error code is
        /// set to ERROR_FILE_NOT_FOUND (2). For more information about devices,
        /// see the Remarks section.
        /// </summary>
        /// <remarks>OPEN_EXISTING</remarks>
        OpenExisting = 3,

        /// <summary>
        /// Opens a file, always. If the specified file exists, the function
        /// succeeds and the last-error code is set to ERROR_ALREADY_EXISTS (183).
        /// If the specified file does not exist and is a valid path to a writable
        /// location, the function creates a file and the last-error code is set to
        /// zero.
        /// </summary>
        /// <remarks>OPEN_ALWAYS</remarks>
        OpenAlways = 4,

        /// <summary>
        /// Opens a file and truncates it so that its size is zero bytes, only if
        /// it exists. If the specified file does not exist, the function fails and
        /// the last-error code is set to ERROR_FILE_NOT_FOUND (2). The calling
        /// process must open the file with the GENERIC_WRITE bit set as part of
        /// the dwDesiredAccess parameter.
        /// </summary>
        /// <remarks>TRUNCATE_EXISTING</remarks>
        TruncateExisting = 5,
    }

    [Flags]
    [Serializable]
    internal enum FileAttributeFlags : uint
    {
        // http://msdn.microsoft.com/en-us/library/windows/desktop/aa363858(v=vs.85).aspx

        #region File Attributes

        // http://msdn.microsoft.com/en-us/library/windows/desktop/gg258117(v=vs.85).aspx

        /// <summary>
        /// A file that is read-only. Applications can read the file, but cannot
        /// write to it or delete it. This attribute is not honored on directories.
        /// For more information, see You cannot view or change the Read-only or
        /// the System attributes of folders in Windows Server 2003, in Windows XP,
        /// in Windows Vista or in Windows 7.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_READONLY</remarks>
        FileAttributeReadonly = 0x1,

        /// <summary>
        /// The file or directory is hidden. It is not included in an ordinary
        /// directory listing.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_HIDDEN</remarks>
        FileAttributeHidden = 0x2,

        /// <summary>
        /// A file or directory that the operating system uses a part of, or uses
        /// exclusively.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_SYSTEM</remarks>
        FileAttributeSystem = 0x4,

        /// <summary>
        /// The handle that identifies a directory.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_DIRECTORY</remarks>
        FileAttributeDirectory = 0x10,

        /// <summary>
        /// A file or directory that is an archive file or directory. Applications
        /// typically use this attribute to mark files for backup or removal.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_ARCHIVE</remarks>
        FileAttributeArchive = 0x20,

        /// <summary>
        /// This value is reserved for system use.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_DEVICE</remarks>
        FileAttributeDevice = 0x40,

        /// <summary>
        /// A file that does not have other attributes set. This attribute is valid
        /// only when used alone.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_NORMAL</remarks>
        FileAttributeNormal = 0x80,

        /// <summary>
        /// A file that is being used for temporary storage. File systems avoid
        /// writing data back to mass storage if sufficient cache memory is
        /// available, because typically, an application deletes a temporary file
        /// after the handle is closed. In that scenario, the system can entirely
        /// avoid writing the data. Otherwise, the data is written after the handle
        /// is closed.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_TEMPORARY</remarks>
        FileAttributeTemporary = 0x100,

        /// <summary>
        /// A file that is a sparse file.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_SPARSE_FILE</remarks>
        FileAttributeSparseFile = 0x200,

        /// <summary>
        /// A file or directory that has an associated reparse point, or a file
        /// that is a symbolic link.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_REPARSE_POINT</remarks>
        FileAttributeReparsePoint = 0x400,

        /// <summary>
        /// A file or directory that is compressed. For a file, all of the data in
        /// the file is compressed. For a directory, compression is the default for
        /// newly created files and subdirectories.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_COMPRESSED</remarks>
        FileAttributeCompressed = 0x800,

        /// <summary>
        /// The data of a file is not immediately available. This attribute
        /// indicates that file data is physically moved to offline storage.
        /// This attribute is used by Remote Storage, the hierarchical storage
        /// management software. Applications should not arbitrarily change this
        /// attribute.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_OFFLINE</remarks>
        FileAttributeOffline = 0x1000,

        /// <summary>
        /// The file or directory is not to be indexed by the content indexing
        /// service.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_NOT_CONTENT_INDEXED</remarks>
        FileAttributeNotContentIndexed = 0x2000,

        /// <summary>
        /// A file or directory that is encrypted. For a file, all data streams in
        /// the file are encrypted. For a directory, encryption is the default for
        /// newly created files and subdirectories.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_ENCRYPTED</remarks>
        FileAttributeEncrypted = 0x4000,

        /// <summary>
        /// The directory or user data stream is configured with integrity (only
        /// supported on ReFS volumes). It is not included in an ordinary directory
        /// listing. The integrity setting persists with the file if it's renamed.
        /// If a file is copied the destination file will have integrity set if
        /// either the source file or destination directory have integrity set.
        /// Windows Server 2008 R2, Windows 7, Windows Server 2008, Windows Vista,
        /// Windows Server 2003, and Windows XP: This flag is not supported until
        /// Windows Server 2012.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_INTEGRITY_STREAM</remarks>
        FileAttributeIntegrityStream = 0x8000,

        /// <summary>
        /// This value is reserved for system use.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_VIRTUAL</remarks>
        FileAttributeVirtual = 0x10000,

        /// <summary>
        /// The user data stream not to be read by the background data integrity
        /// scanner (AKA scrubber). When set on a directory it only provides
        /// inheritance. This flag is only supported on Storage Spaces and ReFS
        /// volumes. It is not included in an ordinary directory listing. Windows
        /// Server 2008 R2, Windows 7, Windows Server 2008, Windows Vista, Windows
        /// Server 2003, and Windows XP: This flag is not supported until Windows
        /// 8 and Windows Server 2012.
        /// </summary>
        /// <remarks>FILE_ATTRIBUTE_NO_SCRUB_DATA</remarks>
        FileAttributeNoScrubData = 0x20000,

        #endregion

        #region File Flags

        /// <summary>
        /// If you attempt to create multiple instances of a pipe with this flag,
        /// creation of the first instance succeeds, but creation of the next
        /// instance fails with ERROR_ACCESS_DENIED. Windows 2000: This flag is
        /// not supported until Windows 2000 SP2 and Windows XP.
        /// </summary>
        /// <remarks>FILE_FLAG_FIRST_PIPE_INSTANCE</remarks>
        FileFlagFirstPipeInstance = 0x00080000,

        /// <summary>
        /// The file data is requested, but it should continue to be located in
        /// remote storage. It should not be transported back to local storage.
        /// This flag is for use by remote storage systems.
        /// </summary>
        /// <remarks>FILE_FLAG_OPEN_NO_RECALL</remarks>
        FileFlagOpenNoRecall = 0x00100000,

        /// <summary>
        /// Access will occur according to POSIX rules. This includes allowing
        /// multiple files with names, differing only in case, for file systems
        /// that support that naming. Use care when using this option, because
        /// files created with this flag may not be accessible by applications
        /// that are written for MS-DOS or 16-bit Windows.
        /// </summary>
        /// <remarks>FILE_FLAG_POSIX_SEMANTICS</remarks>
        FileFlagPosixSemantics = 0x00100000,

        /// <summary>
        /// Normal reparse point processing will not occur; CreateFile will attempt
        /// to open the reparse point. When a file is opened, a file handle is
        /// returned, whether or not the filter that controls the reparse point is
        /// operational. This flag cannot be used with the CREATE_ALWAYS flag. If
        /// the file is not a reparse point, then this flag is ignored. For more
        /// information, see the Remarks section.
        /// </summary>
        /// <remarks>FILE_FLAG_OPEN_REPARSE_POINT</remarks>
        FileFlagOpenReparsePoint = 0x00200000,

        /// <summary>
        /// The file or device is being opened with session awareness. If this flag
        /// is not specified, then per-session devices (such as a redirected USB
        /// device) cannot be opened by processes running in session 0. This flag
        /// has no effect for callers not in session 0. This flag is supported only
        /// on server editions of Windows. Windows Server 2008 R2, Windows Server
        /// 2008, and Windows Server 2003: This flag is not supported before Windows
        /// Server 2012.
        /// </summary>
        /// <remarks>FILE_FLAG_SESSION_AWARE</remarks>
        FileFlagSessionAware = 0x00800000,

        /// <summary>
        /// The file is being opened or created for a backup or restore operation.
        /// The system ensures that the calling process overrides file security
        /// checks when the process has SE_BACKUP_NAME and SE_RESTORE_NAME
        /// privileges. For more information, see Changing Privileges in a Token.
        /// You must set this flag to obtain a handle to a directory. A directory
        /// handle can be passed to some functions instead of a file handle. For
        /// more information, see the Remarks section.
        /// </summary>
        /// <remarks>FILE_FLAG_BACKUP_SEMANTICS</remarks>
        FileFlagBackupSemantics = 0x02000000,

        /// <summary>
        /// The file is to be deleted immediately after all of its handles are
        /// closed, which includes the specified handle and any other open or
        /// duplicated handles. If there are existing open handles to a file, the
        /// call fails unless they were all opened with the FILE_SHARE_DELETE share
        /// mode. Subsequent open requests for the file fail, unless the
        /// FILE_SHARE_DELETE share mode is specified.
        /// </summary>
        /// <remarks>FILE_FLAG_DELETE_ON_CLOSE</remarks>
        FileFlagDeleteOnClose = 0x04000000,

        /// <summary>
        /// Access is intended to be sequential from beginning to end. The system
        /// can use this as a hint to optimize file caching. This flag should not
        /// be used if read-behind (that is, reverse scans) will be used. This flag
        /// has no effect if the file system does not support cached I/O and
        /// FILE_FLAG_NO_BUFFERING. For more information, see the Caching Behavior
        /// section of this topic.
        /// </summary>
        /// <remarks>FILE_FLAG_SEQUENTIAL_SCAN</remarks>
        FileFlagSequentialScan = 0x08000000,

        /// <summary>
        /// Access is intended to be random. The system can use this as a hint to
        /// optimize file caching. This flag has no effect if the file system does
        /// not support cached I/O and FILE_FLAG_NO_BUFFERING. For more information,
        /// see the Caching Behavior section of this topic.
        /// </summary>
        /// <remarks>FILE_FLAG_RANDOM_ACCESS</remarks>
        FileFlagRandomAccess = 0x10000000,

        /// <summary>
        /// The file or device is being opened with no system caching for data
        /// reads and writes. This flag does not affect hard disk caching or memory
        /// mapped files. There are strict requirements for successfully working
        /// with files opened with CreateFile using the FILE_FLAG_NO_BUFFERING
        /// flag, for details see File Buffering.
        /// </summary>
        /// <remarks>FILE_FLAG_NO_BUFFERING</remarks>
        FileFlagNoBuffering = 0x20000000,

        /// <summary>
        /// The file or device is being opened or created for asynchronous I/O.
        /// When subsequent I/O operations are completed on this handle, the event
        /// specified in the OVERLAPPED structure will be set to the signaled state.
        /// If this flag is specified, the file can be used for simultaneous read
        /// and write operations. If this flag is not specified, then I/O operations
        /// are serialized, even if the calls to the read and write functions
        /// specify an OVERLAPPED structure. For information about considerations
        /// when using a file handle created with this flag, see the Synchronous
        /// and Asynchronous I/O Handles section of this topic.
        /// </summary>
        /// <remarks>FILE_FLAG_OVERLAPPED</remarks>
        FileFlagOverlapped = 0x40000000,

        /// <summary>
        /// Write operations will not go through any intermediate cache, they will
        /// go directly to disk. For additional information, see the Caching
        /// Behavior section of this topic.
        /// </summary>
        /// <remarks>FILE_FLAG_WRITE_THROUGH</remarks>
        FileFlagWriteThrough = unchecked(0x80000000),

        #endregion
    }

    [Flags]
    [Serializable]
    internal enum AllocFlags : uint
    {
        //LMEM_FIXED
        Fixed = 0x00,

        //LMEM_MOVEABLE
        Moveable = 0x02,

        //LMEM_ZEROINIT
        ZeroInit = 0x40,
    }

    [Serializable]
    internal enum SymbolicLinkFlag : uint
    {
        File = 0,
        Directory = 1,
    }
}