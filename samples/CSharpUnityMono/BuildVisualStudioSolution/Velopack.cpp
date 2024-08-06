//
//  INTRODUCTION
//
//  This is a library to help developers integrate https://velopack.io into their 
//  applications. Velopack is an update/installer framework for cross-platform 
//  desktop applications. 
//  
//  This library is auto-generated using https://github.com/fusionlanguage/fut
//  and this source file should not be directly modified.
//
//  MIT LICENSE
//
//  Copyright (c) 2024 Caelan Sayler
//
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
//
//  The above copyright notice and this permission notice shall be included in all
//  copies or substantial portions of the Software.
//
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//  SOFTWARE.
//

/*
   The latest version of this library is available on GitHub;
   https://github.com/sheredom/subprocess.h
*/

/*
   This is free and unencumbered software released into the public domain.

   Anyone is free to copy, modify, publish, use, compile, sell, or
   distribute this software, either in source code form or as a compiled
   binary, for any purpose, commercial or non-commercial, and by any
   means.

   In jurisdictions that recognize copyright laws, the author or authors
   of this software dedicate any and all copyright interest in the
   software to the public domain. We make this dedication for the benefit
   of the public at large and to the detriment of our heirs and
   successors. We intend this dedication to be an overt act of
   relinquishment in perpetuity of all present and future rights to this
   software under copyright law.

   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
   EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
   MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
   IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR
   OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
   ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
   OTHER DEALINGS IN THE SOFTWARE.

   For more information, please refer to <http://unlicense.org/>
*/
#include "PrecompiledHeader.h"
#ifndef SHEREDOM_SUBPROCESS_H_INCLUDED
#define SHEREDOM_SUBPROCESS_H_INCLUDED

#if defined(_MSC_VER)
#pragma warning(push, 1)

/* disable warning: '__cplusplus' is not defined as a preprocessor macro,
 * replacing with '0' for '#if/#elif' */
#pragma warning(disable : 4668)
#endif

#include <stdio.h>
#include <string.h>

#if defined(_MSC_VER)
#pragma warning(pop)
#endif

#if defined(__TINYC__)
#define SUBPROCESS_ATTRIBUTE(a) __attribute((a))
#else
#define SUBPROCESS_ATTRIBUTE(a) __attribute__((a))
#endif

#if defined(_MSC_VER)
#define subprocess_pure
#define subprocess_weak __inline
#define subprocess_tls __declspec(thread)
#elif defined(__MINGW32__)
#define subprocess_pure SUBPROCESS_ATTRIBUTE(pure)
#define subprocess_weak static SUBPROCESS_ATTRIBUTE(used)
#define subprocess_tls __thread
#elif defined(__clang__) || defined(__GNUC__) || defined(__TINYC__)
#define subprocess_pure SUBPROCESS_ATTRIBUTE(pure)
#define subprocess_weak SUBPROCESS_ATTRIBUTE(weak)
#define subprocess_tls __thread
#else
#error Non clang, non gcc, non MSVC compiler found!
#endif

struct subprocess_s;

enum subprocess_option_e {
  // stdout and stderr are the same FILE.
  subprocess_option_combined_stdout_stderr = 0x1,

  // The child process should inherit the environment variables of the parent.
  subprocess_option_inherit_environment = 0x2,

  // Enable asynchronous reading of stdout/stderr before it has completed.
  subprocess_option_enable_async = 0x4,

  // Enable the child process to be spawned with no window visible if supported
  // by the platform.
  subprocess_option_no_window = 0x8,

  // Search for program names in the PATH variable. Always enabled on Windows.
  // Note: this will **not** search for paths in any provided custom environment
  // and instead uses the PATH of the spawning process.
  subprocess_option_search_user_path = 0x10
};

#if defined(__cplusplus)
extern "C" {
#endif

/// @brief Create a process.
/// @param command_line An array of strings for the command line to execute for
/// this process. The last element must be NULL to signify the end of the array.
/// The memory backing this parameter only needs to persist until this function
/// returns.
/// @param options A bit field of subprocess_option_e's to pass.
/// @param out_process The newly created process.
/// @return On success zero is returned.
subprocess_weak int subprocess_create(const char *const command_line[],
                                      int options,
                                      struct subprocess_s *const out_process);

/// @brief Create a process (extended create).
/// @param command_line An array of strings for the command line to execute for
/// this process. The last element must be NULL to signify the end of the array.
/// The memory backing this parameter only needs to persist until this function
/// returns.
/// @param options A bit field of subprocess_option_e's to pass.
/// @param environment An optional array of strings for the environment to use
/// for a child process (each element of the form FOO=BAR). The last element
/// must be NULL to signify the end of the array.
/// @param out_process The newly created process.
/// @return On success zero is returned.
///
/// If `options` contains `subprocess_option_inherit_environment`, then
/// `environment` must be NULL.
subprocess_weak int
subprocess_create_ex(const char *const command_line[], int options,
                     const char *const environment[],
                     struct subprocess_s *const out_process);

/// @brief Get the standard input file for a process.
/// @param process The process to query.
/// @return The file for standard input of the process.
///
/// The file returned can be written to by the parent process to feed data to
/// the standard input of the process.
subprocess_pure subprocess_weak FILE *
subprocess_stdin(const struct subprocess_s *const process);

/// @brief Get the standard output file for a process.
/// @param process The process to query.
/// @return The file for standard output of the process.
///
/// The file returned can be read from by the parent process to read data from
/// the standard output of the child process.
subprocess_pure subprocess_weak FILE *
subprocess_stdout(const struct subprocess_s *const process);

/// @brief Get the standard error file for a process.
/// @param process The process to query.
/// @return The file for standard error of the process.
///
/// The file returned can be read from by the parent process to read data from
/// the standard error of the child process.
///
/// If the process was created with the subprocess_option_combined_stdout_stderr
/// option bit set, this function will return NULL, and the subprocess_stdout
/// function should be used for both the standard output and error combined.
subprocess_pure subprocess_weak FILE *
subprocess_stderr(const struct subprocess_s *const process);

/// @brief Wait for a process to finish execution.
/// @param process The process to wait for.
/// @param out_return_code The return code of the returned process (can be
/// NULL).
/// @return On success zero is returned.
///
/// Joining a process will close the stdin pipe to the process.
subprocess_weak int subprocess_join(struct subprocess_s *const process,
                                    int *const out_return_code);

/// @brief Destroy a previously created process.
/// @param process The process to destroy.
/// @return On success zero is returned.
///
/// If the process to be destroyed had not finished execution, it may out live
/// the parent process.
subprocess_weak int subprocess_destroy(struct subprocess_s *const process);

/// @brief Terminate a previously created process.
/// @param process The process to terminate.
/// @return On success zero is returned.
///
/// If the process to be destroyed had not finished execution, it will be
/// terminated (i.e killed).
subprocess_weak int subprocess_terminate(struct subprocess_s *const process);

/// @brief Read the standard output from the child process.
/// @param process The process to read from.
/// @param buffer The buffer to read into.
/// @param size The maximum number of bytes to read.
/// @return The number of bytes actually read into buffer. Can only be 0 if the
/// process has complete.
///
/// The only safe way to read from the standard output of a process during it's
/// execution is to use the `subprocess_option_enable_async` option in
/// conjunction with this method.
subprocess_weak unsigned
subprocess_read_stdout(struct subprocess_s *const process, char *const buffer,
                       unsigned size);

/// @brief Read the standard error from the child process.
/// @param process The process to read from.
/// @param buffer The buffer to read into.
/// @param size The maximum number of bytes to read.
/// @return The number of bytes actually read into buffer. Can only be 0 if the
/// process has complete.
///
/// The only safe way to read from the standard error of a process during it's
/// execution is to use the `subprocess_option_enable_async` option in
/// conjunction with this method.
subprocess_weak unsigned
subprocess_read_stderr(struct subprocess_s *const process, char *const buffer,
                       unsigned size);

/// @brief Returns if the subprocess is currently still alive and executing.
/// @param process The process to check.
/// @return If the process is still alive non-zero is returned.
subprocess_weak int subprocess_alive(struct subprocess_s *const process);

#if defined(__cplusplus)
#define SUBPROCESS_CAST(type, x) static_cast<type>(x)
#define SUBPROCESS_PTR_CAST(type, x) reinterpret_cast<type>(x)
#define SUBPROCESS_CONST_CAST(type, x) const_cast<type>(x)
#define SUBPROCESS_NULL NULL
#else
#define SUBPROCESS_CAST(type, x) ((type)(x))
#define SUBPROCESS_PTR_CAST(type, x) ((type)(x))
#define SUBPROCESS_CONST_CAST(type, x) ((type)(x))
#define SUBPROCESS_NULL 0
#endif

#if !defined(_WIN32)
#include <signal.h>
#include <spawn.h>
#include <stdlib.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>
#endif

#if defined(_WIN32)

#if (_MSC_VER < 1920)
#ifdef _WIN64
typedef __int64 subprocess_intptr_t;
typedef unsigned __int64 subprocess_size_t;
#else
typedef int subprocess_intptr_t;
typedef unsigned int subprocess_size_t;
#endif
#else
#include <inttypes.h>

typedef intptr_t subprocess_intptr_t;
typedef size_t subprocess_size_t;
#endif

#ifdef __clang__
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wreserved-identifier"
#endif

typedef struct _PROCESS_INFORMATION *LPPROCESS_INFORMATION;
typedef struct _SECURITY_ATTRIBUTES *LPSECURITY_ATTRIBUTES;
typedef struct _STARTUPINFOA *LPSTARTUPINFOA;
typedef struct _OVERLAPPED *LPOVERLAPPED;

#ifdef __clang__
#pragma clang diagnostic pop
#endif

#ifdef _MSC_VER
#pragma warning(push, 1)
#endif
#ifdef __MINGW32__
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wpedantic"
#endif

struct subprocess_subprocess_information_s {
  void *hProcess;
  void *hThread;
  unsigned long dwProcessId;
  unsigned long dwThreadId;
};

struct subprocess_security_attributes_s {
  unsigned long nLength;
  void *lpSecurityDescriptor;
  int bInheritHandle;
};

struct subprocess_startup_info_s {
  unsigned long cb;
  char *lpReserved;
  char *lpDesktop;
  char *lpTitle;
  unsigned long dwX;
  unsigned long dwY;
  unsigned long dwXSize;
  unsigned long dwYSize;
  unsigned long dwXCountChars;
  unsigned long dwYCountChars;
  unsigned long dwFillAttribute;
  unsigned long dwFlags;
  unsigned short wShowWindow;
  unsigned short cbReserved2;
  unsigned char *lpReserved2;
  void *hStdInput;
  void *hStdOutput;
  void *hStdError;
};

struct subprocess_overlapped_s {
  uintptr_t Internal;
  uintptr_t InternalHigh;
  union {
    struct {
      unsigned long Offset;
      unsigned long OffsetHigh;
    } DUMMYSTRUCTNAME;
    void *Pointer;
  } DUMMYUNIONNAME;

  void *hEvent;
};

#ifdef __MINGW32__
#pragma GCC diagnostic pop
#endif
#ifdef _MSC_VER
#pragma warning(pop)
#endif

__declspec(dllimport) unsigned long __stdcall GetLastError(void);
__declspec(dllimport) int __stdcall SetHandleInformation(void *, unsigned long,
                                                         unsigned long);
__declspec(dllimport) int __stdcall CreatePipe(void **, void **,
                                               LPSECURITY_ATTRIBUTES,
                                               unsigned long);
__declspec(dllimport) void *__stdcall CreateNamedPipeA(
    const char *, unsigned long, unsigned long, unsigned long, unsigned long,
    unsigned long, unsigned long, LPSECURITY_ATTRIBUTES);
__declspec(dllimport) int __stdcall ReadFile(void *, void *, unsigned long,
                                             unsigned long *, LPOVERLAPPED);
__declspec(dllimport) unsigned long __stdcall GetCurrentProcessId(void);
__declspec(dllimport) unsigned long __stdcall GetCurrentThreadId(void);
__declspec(dllimport) void *__stdcall CreateFileA(const char *, unsigned long,
                                                  unsigned long,
                                                  LPSECURITY_ATTRIBUTES,
                                                  unsigned long, unsigned long,
                                                  void *);
__declspec(dllimport) void *__stdcall CreateEventA(LPSECURITY_ATTRIBUTES, int,
                                                   int, const char *);
__declspec(dllimport) int __stdcall CreateProcessA(
    const char *, char *, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES, int,
    unsigned long, void *, const char *, LPSTARTUPINFOA, LPPROCESS_INFORMATION);
__declspec(dllimport) int __stdcall CloseHandle(void *);
__declspec(dllimport) unsigned long __stdcall WaitForSingleObject(
    void *, unsigned long);
__declspec(dllimport) int __stdcall GetExitCodeProcess(
    void *, unsigned long *lpExitCode);
__declspec(dllimport) int __stdcall TerminateProcess(void *, unsigned int);
__declspec(dllimport) unsigned long __stdcall WaitForMultipleObjects(
    unsigned long, void *const *, int, unsigned long);
__declspec(dllimport) int __stdcall GetOverlappedResult(void *, LPOVERLAPPED,
                                                        unsigned long *, int);

#if defined(_DLL)
#define SUBPROCESS_DLLIMPORT __declspec(dllimport)
#else
#define SUBPROCESS_DLLIMPORT
#endif

#ifdef __clang__
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wreserved-identifier"
#endif

SUBPROCESS_DLLIMPORT int __cdecl _fileno(FILE *);
SUBPROCESS_DLLIMPORT int __cdecl _open_osfhandle(subprocess_intptr_t, int);
SUBPROCESS_DLLIMPORT subprocess_intptr_t __cdecl _get_osfhandle(int);

#ifndef __MINGW32__
void *__cdecl _alloca(subprocess_size_t);
#else
#include <malloc.h>
#endif

#ifdef __clang__
#pragma clang diagnostic pop
#endif

#else
typedef size_t subprocess_size_t;
#endif

#ifdef __clang__
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wpadded"
#endif
struct subprocess_s {
  FILE *stdin_file;
  FILE *stdout_file;
  FILE *stderr_file;

#if defined(_WIN32)
  void *hProcess;
  void *hStdInput;
  void *hEventOutput;
  void *hEventError;
#else
  pid_t child;
  int return_status;
#endif

  subprocess_size_t alive;
};
#ifdef __clang__
#pragma clang diagnostic pop
#endif

#if defined(__clang__)
#if __has_warning("-Wunsafe-buffer-usage")
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wunsafe-buffer-usage"
#endif
#endif

#if defined(_WIN32)
subprocess_weak int subprocess_create_named_pipe_helper(void **rd, void **wr);
int subprocess_create_named_pipe_helper(void **rd, void **wr) {
  const unsigned long pipeAccessInbound = 0x00000001;
  const unsigned long fileFlagOverlapped = 0x40000000;
  const unsigned long pipeTypeByte = 0x00000000;
  const unsigned long pipeWait = 0x00000000;
  const unsigned long genericWrite = 0x40000000;
  const unsigned long openExisting = 3;
  const unsigned long fileAttributeNormal = 0x00000080;
  const void *const invalidHandleValue =
      SUBPROCESS_PTR_CAST(void *, ~(SUBPROCESS_CAST(subprocess_intptr_t, 0)));
  struct subprocess_security_attributes_s saAttr = {sizeof(saAttr),
                                                    SUBPROCESS_NULL, 1};
  char name[256] = {0};
  static subprocess_tls long index = 0;
  const long unique = index++;

#if defined(_MSC_VER) && _MSC_VER < 1900
#pragma warning(push, 1)
#pragma warning(disable : 4996)
  _snprintf(name, sizeof(name) - 1,
            "\\\\.\\pipe\\sheredom_subprocess_h.%08lx.%08lx.%ld",
            GetCurrentProcessId(), GetCurrentThreadId(), unique);
#pragma warning(pop)
#else
  snprintf(name, sizeof(name) - 1,
           "\\\\.\\pipe\\sheredom_subprocess_h.%08lx.%08lx.%ld",
           GetCurrentProcessId(), GetCurrentThreadId(), unique);
#endif

  *rd =
      CreateNamedPipeA(name, pipeAccessInbound | fileFlagOverlapped,
                       pipeTypeByte | pipeWait, 1, 4096, 4096, SUBPROCESS_NULL,
                       SUBPROCESS_PTR_CAST(LPSECURITY_ATTRIBUTES, &saAttr));

  if (invalidHandleValue == *rd) {
    return -1;
  }

  *wr = CreateFileA(name, genericWrite, SUBPROCESS_NULL,
                    SUBPROCESS_PTR_CAST(LPSECURITY_ATTRIBUTES, &saAttr),
                    openExisting, fileAttributeNormal, SUBPROCESS_NULL);

  if (invalidHandleValue == *wr) {
    return -1;
  }

  return 0;
}
#endif

int subprocess_create(const char *const commandLine[], int options,
                      struct subprocess_s *const out_process) {
  return subprocess_create_ex(commandLine, options, SUBPROCESS_NULL,
                              out_process);
}

int subprocess_create_ex(const char *const commandLine[], int options,
                         const char *const environment[],
                         struct subprocess_s *const out_process) {
#if defined(_WIN32)
  int fd;
  void *rd, *wr;
  char *commandLineCombined;
  subprocess_size_t len;
  int i, j;
  int need_quoting;
  unsigned long flags = 0;
  const unsigned long startFUseStdHandles = 0x00000100;
  const unsigned long handleFlagInherit = 0x00000001;
  const unsigned long createNoWindow = 0x08000000;
  struct subprocess_subprocess_information_s processInfo;
  struct subprocess_security_attributes_s saAttr = {sizeof(saAttr),
                                                    SUBPROCESS_NULL, 1};
  char *used_environment = SUBPROCESS_NULL;
  struct subprocess_startup_info_s startInfo = {0,
                                                SUBPROCESS_NULL,
                                                SUBPROCESS_NULL,
                                                SUBPROCESS_NULL,
                                                0,
                                                0,
                                                0,
                                                0,
                                                0,
                                                0,
                                                0,
                                                0,
                                                0,
                                                0,
                                                SUBPROCESS_NULL,
                                                SUBPROCESS_NULL,
                                                SUBPROCESS_NULL,
                                                SUBPROCESS_NULL};

  startInfo.cb = sizeof(startInfo);
  startInfo.dwFlags = startFUseStdHandles;

  if (subprocess_option_no_window == (options & subprocess_option_no_window)) {
    flags |= createNoWindow;
  }

  if (subprocess_option_inherit_environment !=
      (options & subprocess_option_inherit_environment)) {
    if (SUBPROCESS_NULL == environment) {
      used_environment = SUBPROCESS_CONST_CAST(char *, "\0\0");
    } else {
      // We always end with two null terminators.
      len = 2;

      for (i = 0; environment[i]; i++) {
        for (j = 0; '\0' != environment[i][j]; j++) {
          len++;
        }

        // For the null terminator too.
        len++;
      }

      used_environment = SUBPROCESS_CAST(char *, _alloca(len));

      // Re-use len for the insertion position
      len = 0;

      for (i = 0; environment[i]; i++) {
        for (j = 0; '\0' != environment[i][j]; j++) {
          used_environment[len++] = environment[i][j];
        }

        used_environment[len++] = '\0';
      }

      // End with the two null terminators.
      used_environment[len++] = '\0';
      used_environment[len++] = '\0';
    }
  } else {
    if (SUBPROCESS_NULL != environment) {
      return -1;
    }
  }

  if (!CreatePipe(&rd, &wr, SUBPROCESS_PTR_CAST(LPSECURITY_ATTRIBUTES, &saAttr),
                  0)) {
    return -1;
  }

  if (!SetHandleInformation(wr, handleFlagInherit, 0)) {
    return -1;
  }

  fd = _open_osfhandle(SUBPROCESS_PTR_CAST(subprocess_intptr_t, wr), 0);

  if (-1 != fd) {
    out_process->stdin_file = _fdopen(fd, "wb");

    if (SUBPROCESS_NULL == out_process->stdin_file) {
      return -1;
    }
  }

  startInfo.hStdInput = rd;

  if (options & subprocess_option_enable_async) {
    if (subprocess_create_named_pipe_helper(&rd, &wr)) {
      return -1;
    }
  } else {
    if (!CreatePipe(&rd, &wr,
                    SUBPROCESS_PTR_CAST(LPSECURITY_ATTRIBUTES, &saAttr), 0)) {
      return -1;
    }
  }

  if (!SetHandleInformation(rd, handleFlagInherit, 0)) {
    return -1;
  }

  fd = _open_osfhandle(SUBPROCESS_PTR_CAST(subprocess_intptr_t, rd), 0);

  if (-1 != fd) {
    out_process->stdout_file = _fdopen(fd, "rb");

    if (SUBPROCESS_NULL == out_process->stdout_file) {
      return -1;
    }
  }

  startInfo.hStdOutput = wr;

  if (subprocess_option_combined_stdout_stderr ==
      (options & subprocess_option_combined_stdout_stderr)) {
    out_process->stderr_file = out_process->stdout_file;
    startInfo.hStdError = startInfo.hStdOutput;
  } else {
    if (options & subprocess_option_enable_async) {
      if (subprocess_create_named_pipe_helper(&rd, &wr)) {
        return -1;
      }
    } else {
      if (!CreatePipe(&rd, &wr,
                      SUBPROCESS_PTR_CAST(LPSECURITY_ATTRIBUTES, &saAttr), 0)) {
        return -1;
      }
    }

    if (!SetHandleInformation(rd, handleFlagInherit, 0)) {
      return -1;
    }

    fd = _open_osfhandle(SUBPROCESS_PTR_CAST(subprocess_intptr_t, rd), 0);

    if (-1 != fd) {
      out_process->stderr_file = _fdopen(fd, "rb");

      if (SUBPROCESS_NULL == out_process->stderr_file) {
        return -1;
      }
    }

    startInfo.hStdError = wr;
  }

  if (options & subprocess_option_enable_async) {
    out_process->hEventOutput =
        CreateEventA(SUBPROCESS_PTR_CAST(LPSECURITY_ATTRIBUTES, &saAttr), 1, 1,
                     SUBPROCESS_NULL);
    out_process->hEventError =
        CreateEventA(SUBPROCESS_PTR_CAST(LPSECURITY_ATTRIBUTES, &saAttr), 1, 1,
                     SUBPROCESS_NULL);
  } else {
    out_process->hEventOutput = SUBPROCESS_NULL;
    out_process->hEventError = SUBPROCESS_NULL;
  }

  // Combine commandLine together into a single string
  len = 0;
  for (i = 0; commandLine[i]; i++) {
    // for the trailing \0
    len++;

    // Quote the argument if it has a space in it
    if (strpbrk(commandLine[i], "\t\v ") != SUBPROCESS_NULL)
      len += 2;

    for (j = 0; '\0' != commandLine[i][j]; j++) {
      switch (commandLine[i][j]) {
      default:
        break;
      case '\\':
        if (commandLine[i][j + 1] == '"') {
          len++;
        }

        break;
      case '"':
        len++;
        break;
      }
      len++;
    }
  }

  commandLineCombined = SUBPROCESS_CAST(char *, _alloca(len));

  if (!commandLineCombined) {
    return -1;
  }

  // Gonna re-use len to store the write index into commandLineCombined
  len = 0;

  for (i = 0; commandLine[i]; i++) {
    if (0 != i) {
      commandLineCombined[len++] = ' ';
    }

    need_quoting = strpbrk(commandLine[i], "\t\v ") != SUBPROCESS_NULL;
    if (need_quoting) {
      commandLineCombined[len++] = '"';
    }

    for (j = 0; '\0' != commandLine[i][j]; j++) {
      switch (commandLine[i][j]) {
      default:
        break;
      case '\\':
        if (commandLine[i][j + 1] == '"') {
          commandLineCombined[len++] = '\\';
        }

        break;
      case '"':
        commandLineCombined[len++] = '\\';
        break;
      }

      commandLineCombined[len++] = commandLine[i][j];
    }
    if (need_quoting) {
      commandLineCombined[len++] = '"';
    }
  }

  commandLineCombined[len] = '\0';

  if (!CreateProcessA(
          SUBPROCESS_NULL,
          commandLineCombined, // command line
          SUBPROCESS_NULL,     // process security attributes
          SUBPROCESS_NULL,     // primary thread security attributes
          1,                   // handles are inherited
          flags,               // creation flags
          used_environment,    // used environment
          SUBPROCESS_NULL,     // use parent's current directory
          SUBPROCESS_PTR_CAST(LPSTARTUPINFOA,
                              &startInfo), // STARTUPINFO pointer
          SUBPROCESS_PTR_CAST(LPPROCESS_INFORMATION, &processInfo))) {
    return -1;
  }

  out_process->hProcess = processInfo.hProcess;

  out_process->hStdInput = startInfo.hStdInput;

  // We don't need the handle of the primary thread in the called process.
  CloseHandle(processInfo.hThread);

  if (SUBPROCESS_NULL != startInfo.hStdOutput) {
    CloseHandle(startInfo.hStdOutput);

    if (startInfo.hStdError != startInfo.hStdOutput) {
      CloseHandle(startInfo.hStdError);
    }
  }

  out_process->alive = 1;

  return 0;
#else
  int stdinfd[2];
  int stdoutfd[2];
  int stderrfd[2];
  pid_t child;
  extern char **environ;
  char *const empty_environment[1] = {SUBPROCESS_NULL};
  posix_spawn_file_actions_t actions;
  char *const *used_environment;

  if (subprocess_option_inherit_environment ==
      (options & subprocess_option_inherit_environment)) {
    if (SUBPROCESS_NULL != environment) {
      return -1;
    }
  }

  if (0 != pipe(stdinfd)) {
    return -1;
  }

  if (0 != pipe(stdoutfd)) {
    return -1;
  }

  if (subprocess_option_combined_stdout_stderr !=
      (options & subprocess_option_combined_stdout_stderr)) {
    if (0 != pipe(stderrfd)) {
      return -1;
    }
  }

  if (environment) {
#ifdef __clang__
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wcast-qual"
#pragma clang diagnostic ignored "-Wold-style-cast"
#endif
    used_environment = (char *const *)environment;
#ifdef __clang__
#pragma clang diagnostic pop
#endif
  } else if (subprocess_option_inherit_environment ==
             (options & subprocess_option_inherit_environment)) {
    used_environment = environ;
  } else {
    used_environment = empty_environment;
  }

  if (0 != posix_spawn_file_actions_init(&actions)) {
    return -1;
  }

  // Close the stdin write end
  if (0 != posix_spawn_file_actions_addclose(&actions, stdinfd[1])) {
    posix_spawn_file_actions_destroy(&actions);
    return -1;
  }

  // Map the read end to stdin
  if (0 !=
      posix_spawn_file_actions_adddup2(&actions, stdinfd[0], STDIN_FILENO)) {
    posix_spawn_file_actions_destroy(&actions);
    return -1;
  }

  // Close the stdout read end
  if (0 != posix_spawn_file_actions_addclose(&actions, stdoutfd[0])) {
    posix_spawn_file_actions_destroy(&actions);
    return -1;
  }

  // Map the write end to stdout
  if (0 !=
      posix_spawn_file_actions_adddup2(&actions, stdoutfd[1], STDOUT_FILENO)) {
    posix_spawn_file_actions_destroy(&actions);
    return -1;
  }

  if (subprocess_option_combined_stdout_stderr ==
      (options & subprocess_option_combined_stdout_stderr)) {
    if (0 != posix_spawn_file_actions_adddup2(&actions, STDOUT_FILENO,
                                              STDERR_FILENO)) {
      posix_spawn_file_actions_destroy(&actions);
      return -1;
    }
  } else {
    // Close the stderr read end
    if (0 != posix_spawn_file_actions_addclose(&actions, stderrfd[0])) {
      posix_spawn_file_actions_destroy(&actions);
      return -1;
    }
    // Map the write end to stdout
    if (0 != posix_spawn_file_actions_adddup2(&actions, stderrfd[1],
                                              STDERR_FILENO)) {
      posix_spawn_file_actions_destroy(&actions);
      return -1;
    }
  }

#ifdef __clang__
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wcast-qual"
#pragma clang diagnostic ignored "-Wold-style-cast"
#endif
  if (subprocess_option_search_user_path ==
      (options & subprocess_option_search_user_path)) {
    if (0 != posix_spawnp(&child, commandLine[0], &actions, SUBPROCESS_NULL,
                          (char *const *)commandLine, used_environment)) {
      posix_spawn_file_actions_destroy(&actions);
      return -1;
    }
  } else {
    if (0 != posix_spawn(&child, commandLine[0], &actions, SUBPROCESS_NULL,
                         (char *const *)commandLine, used_environment)) {
      posix_spawn_file_actions_destroy(&actions);
      return -1;
    }
  }
#ifdef __clang__
#pragma clang diagnostic pop
#endif

  // Close the stdin read end
  close(stdinfd[0]);
  // Store the stdin write end
  out_process->stdin_file = fdopen(stdinfd[1], "wb");

  // Close the stdout write end
  close(stdoutfd[1]);
  // Store the stdout read end
  out_process->stdout_file = fdopen(stdoutfd[0], "rb");

  if (subprocess_option_combined_stdout_stderr ==
      (options & subprocess_option_combined_stdout_stderr)) {
    out_process->stderr_file = out_process->stdout_file;
  } else {
    // Close the stderr write end
    close(stderrfd[1]);
    // Store the stderr read end
    out_process->stderr_file = fdopen(stderrfd[0], "rb");
  }

  // Store the child's pid
  out_process->child = child;

  out_process->alive = 1;

  posix_spawn_file_actions_destroy(&actions);
  return 0;
#endif
}

FILE *subprocess_stdin(const struct subprocess_s *const process) {
  return process->stdin_file;
}

FILE *subprocess_stdout(const struct subprocess_s *const process) {
  return process->stdout_file;
}

FILE *subprocess_stderr(const struct subprocess_s *const process) {
  if (process->stdout_file != process->stderr_file) {
    return process->stderr_file;
  } else {
    return SUBPROCESS_NULL;
  }
}

int subprocess_join(struct subprocess_s *const process,
                    int *const out_return_code) {
#if defined(_WIN32)
  const unsigned long infinite = 0xFFFFFFFF;

  if (process->stdin_file) {
    fclose(process->stdin_file);
    process->stdin_file = SUBPROCESS_NULL;
  }

  if (process->hStdInput) {
    CloseHandle(process->hStdInput);
    process->hStdInput = SUBPROCESS_NULL;
  }

  WaitForSingleObject(process->hProcess, infinite);

  if (out_return_code) {
    if (!GetExitCodeProcess(
            process->hProcess,
            SUBPROCESS_PTR_CAST(unsigned long *, out_return_code))) {
      return -1;
    }
  }

  process->alive = 0;

  return 0;
#else
  int status;

  if (process->stdin_file) {
    fclose(process->stdin_file);
    process->stdin_file = SUBPROCESS_NULL;
  }

  if (process->child) {
    if (process->child != waitpid(process->child, &status, 0)) {
      return -1;
    }

    process->child = 0;

    if (WIFEXITED(status)) {
      process->return_status = WEXITSTATUS(status);
    } else {
      process->return_status = EXIT_FAILURE;
    }

    process->alive = 0;
  }

  if (out_return_code) {
    *out_return_code = process->return_status;
  }

  return 0;
#endif
}

int subprocess_destroy(struct subprocess_s *const process) {
  if (process->stdin_file) {
    fclose(process->stdin_file);
    process->stdin_file = SUBPROCESS_NULL;
  }

  if (process->stdout_file) {
    fclose(process->stdout_file);

    if (process->stdout_file != process->stderr_file) {
      fclose(process->stderr_file);
    }

    process->stdout_file = SUBPROCESS_NULL;
    process->stderr_file = SUBPROCESS_NULL;
  }

#if defined(_WIN32)
  if (process->hProcess) {
    CloseHandle(process->hProcess);
    process->hProcess = SUBPROCESS_NULL;

    if (process->hStdInput) {
      CloseHandle(process->hStdInput);
    }

    if (process->hEventOutput) {
      CloseHandle(process->hEventOutput);
    }

    if (process->hEventError) {
      CloseHandle(process->hEventError);
    }
  }
#endif

  return 0;
}

int subprocess_terminate(struct subprocess_s *const process) {
#if defined(_WIN32)
  unsigned int killed_process_exit_code;
  int success_terminate;
  int windows_call_result;

  killed_process_exit_code = 99;
  windows_call_result =
      TerminateProcess(process->hProcess, killed_process_exit_code);
  success_terminate = (windows_call_result == 0) ? 1 : 0;
  return success_terminate;
#else
  int result;
  result = kill(process->child, 9);
  return result;
#endif
}

unsigned subprocess_read_stdout(struct subprocess_s *const process,
                                char *const buffer, unsigned size) {
#if defined(_WIN32)
  void *handle;
  unsigned long bytes_read = 0;
  struct subprocess_overlapped_s overlapped = {0, 0, {{0, 0}}, SUBPROCESS_NULL};
  overlapped.hEvent = process->hEventOutput;

  handle = SUBPROCESS_PTR_CAST(void *,
                               _get_osfhandle(_fileno(process->stdout_file)));

  if (!ReadFile(handle, buffer, size, &bytes_read,
                SUBPROCESS_PTR_CAST(LPOVERLAPPED, &overlapped))) {
    const unsigned long errorIoPending = 997;
    unsigned long error = GetLastError();

    // Means we've got an async read!
    if (error == errorIoPending) {
      if (!GetOverlappedResult(handle,
                               SUBPROCESS_PTR_CAST(LPOVERLAPPED, &overlapped),
                               &bytes_read, 1)) {
        const unsigned long errorIoIncomplete = 996;
        const unsigned long errorHandleEOF = 38;
        error = GetLastError();

        if ((error != errorIoIncomplete) && (error != errorHandleEOF)) {
          return 0;
        }
      }
    }
  }

  return SUBPROCESS_CAST(unsigned, bytes_read);
#else
  const int fd = fileno(process->stdout_file);
  const ssize_t bytes_read = read(fd, buffer, size);

  if (bytes_read < 0) {
    return 0;
  }

  return SUBPROCESS_CAST(unsigned, bytes_read);
#endif
}

unsigned subprocess_read_stderr(struct subprocess_s *const process,
                                char *const buffer, unsigned size) {
#if defined(_WIN32)
  void *handle;
  unsigned long bytes_read = 0;
  struct subprocess_overlapped_s overlapped = {0, 0, {{0, 0}}, SUBPROCESS_NULL};
  overlapped.hEvent = process->hEventError;

  handle = SUBPROCESS_PTR_CAST(void *,
                               _get_osfhandle(_fileno(process->stderr_file)));

  if (!ReadFile(handle, buffer, size, &bytes_read,
                SUBPROCESS_PTR_CAST(LPOVERLAPPED, &overlapped))) {
    const unsigned long errorIoPending = 997;
    unsigned long error = GetLastError();

    // Means we've got an async read!
    if (error == errorIoPending) {
      if (!GetOverlappedResult(handle,
                               SUBPROCESS_PTR_CAST(LPOVERLAPPED, &overlapped),
                               &bytes_read, 1)) {
        const unsigned long errorIoIncomplete = 996;
        const unsigned long errorHandleEOF = 38;
        error = GetLastError();

        if ((error != errorIoIncomplete) && (error != errorHandleEOF)) {
          return 0;
        }
      }
    }
  }

  return SUBPROCESS_CAST(unsigned, bytes_read);
#else
  const int fd = fileno(process->stderr_file);
  const ssize_t bytes_read = read(fd, buffer, size);

  if (bytes_read < 0) {
    return 0;
  }

  return SUBPROCESS_CAST(unsigned, bytes_read);
#endif
}

int subprocess_alive(struct subprocess_s *const process) {
  int is_alive = SUBPROCESS_CAST(int, process->alive);

  if (!is_alive) {
    return 0;
  }
#if defined(_WIN32)
  {
    const unsigned long zero = 0x0;
    const unsigned long wait_object_0 = 0x00000000L;

    is_alive = wait_object_0 != WaitForSingleObject(process->hProcess, zero);
  }
#else
  {
    int status;
    is_alive = 0 == waitpid(process->child, &status, WNOHANG);

    // If the process was successfully waited on we need to cleanup now.
    if (!is_alive) {
      if (WIFEXITED(status)) {
        process->return_status = WEXITSTATUS(status);
      } else {
        process->return_status = EXIT_FAILURE;
      }

      // Since we've already successfully waited on the process, we need to wipe
      // the child now.
      process->child = 0;

      if (subprocess_join(process, SUBPROCESS_NULL)) {
        return -1;
      }
    }
  }
#endif

  if (!is_alive) {
    process->alive = 0;
  }

  return is_alive;
}

#if defined(__clang__)
#if __has_warning("-Wunsafe-buffer-usage")
#pragma clang diagnostic pop
#endif
#endif

#if defined(__cplusplus)
} // extern "C"
#endif

#endif /* SHEREDOM_SUBPROCESS_H_INCLUDED */

#include <string>
#include <filesystem>
#include <algorithm>
#include <cctype>
#include <stdexcept>
#include <functional>
#include <iostream>
#include <fstream>
#include <sstream>
#include <thread>
#include "Velopack.hpp"
// #include "subprocess.h"

// platform-specific includes
#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define PATH_MAX MAX_PATH
#include <Windows.h> // For GetCurrentProcessId, GetModuleFileName, MultiByteToWideChar, WideCharToMultiByte, LCMapStringEx
#elif defined(__unix__) || defined(__APPLE__)
#include <unistd.h>  // For getpid
#include <libproc.h> // For proc_pidpath
#endif

// unicode string manipulation support
#if defined(QT_CORE_LIB)

#include <QString>
static std::string VeloString_ToLower(std::string_view s)
{
    QString t = QString::fromStdString(std::string { s });
    return t.toLower().toStdString();
}

static std::string VeloString_ToUpper(std::string_view s)
{
    QString t = QString::fromStdString(std::string { s });
    return t.toUpper().toStdString();
}

#elif defined(_WIN32)

#include <Windows.h>
static std::string VeloString_Win32LCMap(std::string_view s, DWORD flags)
{
    int size = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
    std::wstring wide(size, 0);
    MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), wide.data(), size);
    size = LCMapStringEx(LOCALE_NAME_SYSTEM_DEFAULT, LCMAP_LINGUISTIC_CASING | flags, wide.data(), size, nullptr, 0, nullptr, nullptr, 0);
    std::wstring wideResult(size, 0);
    LCMapStringEx(LOCALE_NAME_SYSTEM_DEFAULT, LCMAP_LINGUISTIC_CASING | flags, wide.data(), (int)wide.size(), wideResult.data(), size, nullptr, nullptr, 0);
    int resultSize = WideCharToMultiByte(CP_UTF8, 0, wideResult.data(), size, nullptr, 0, nullptr, nullptr);
    std::string result(resultSize, 0);
    WideCharToMultiByte(CP_UTF8, 0, wideResult.data(), size, result.data(), resultSize, nullptr, nullptr);
    return result;
}

static std::string VeloString_ToLower(std::string_view s)
{
    return VeloString_Win32LCMap(s, LCMAP_LOWERCASE);
}

static std::string VeloString_ToUpper(std::string_view s)
{
    return VeloString_Win32LCMap(s, LCMAP_UPPERCASE);
}

#elif defined(VELOPACK_NO_ICU)

static std::string VeloString_ToLower(std::string_view s)
{
    std::string data(s);
    std::transform(data.begin(), data.end(), data.begin(), [](unsigned char c){ return std::tolower(c); });
    return data;
}

static std::string VeloString_ToUpper(std::string_view s)
{
    std::string data(s);
    std::transform(data.begin(), data.end(), data.begin(), [](unsigned char c){ return std::toupper(c); });
    return data;
}

#else

#include <unicode/unistr.h>

static std::string VeloString_ToLower(std::string_view s)
{
    std::string result;
    return icu::UnicodeString::fromUTF8(s).toLower().toUTF8String(result);
}

static std::string VeloString_ToUpper(std::string_view s)
{
    std::string result;
    return icu::UnicodeString::fromUTF8(s).toUpper().toUTF8String(result);
}

#endif

static std::string nativeCurrentOsName()
{
#if defined(__APPLE__)
    return "darwin";
#elif defined(_WIN32)
    return "win32";
#else
    return "linux";
#endif
}

static bool nativeDoesFileExist(std::string file_path)
{
    return std::filesystem::exists(file_path);
}

static void nativeExitProcess(int exit_code)
{
    ::exit(exit_code);
}

static int32_t nativeCurrentProcessId()
{
#if defined(_WIN32)
    return GetCurrentProcessId();
#elif defined(__unix__) || defined(__APPLE__) && defined(__MACH__)
    return getpid();
#else
#error "Unsupported platform"
    return -1; // Indicate error or unsupported platform
#endif
}

static std::string nativeGetCurrentProcessPath()
{
    const size_t buf_size = PATH_MAX;
    char path_buf[buf_size];
    size_t bytes_read = buf_size;

#if defined(_WIN32)
    HMODULE hMod = GetModuleHandleA(NULL);
    bytes_read = GetModuleFileNameA(hMod, path_buf, buf_size);
#else
    // Inspired by: https://stackoverflow.com/a/8149380
    bytes_read = proc_pidpath(getpid(), path_buf, sizeof(path_buf));
    if (bytes_read <= 0)
    {
        throw std::runtime_error("Can't find current process path");
    }
#endif

    return std::string(path_buf, bytes_read);
}

static subprocess_s nativeStartProcess(const std::vector<std::string> *command_line, int options)
{
    auto size = command_line->size();
    const char **command_line_array = new const char *[size + 1];
    for (size_t i = 0; i < size; ++i)
    {
        command_line_array[i] = command_line->at(i).c_str();
    }
    command_line_array[size] = NULL; // last element must be NULL

    struct subprocess_s subprocess;
    int result = subprocess_create(command_line_array, options, &subprocess);
    delete[] command_line_array; // clean up the array

    if (result != 0)
    {
        throw std::runtime_error("Unable to start process.");
    }

    return subprocess;
}

static void nativeStartProcessFireAndForget(const std::vector<std::string> *command_line)
{
    nativeStartProcess(command_line, subprocess_option_no_window | subprocess_option_inherit_environment);
}

static std::string nativeStartProcessBlocking(const std::vector<std::string> *command_line)
{
    subprocess_s subprocess = nativeStartProcess(command_line, subprocess_option_no_window | subprocess_option_inherit_environment);
    FILE *p_stdout = subprocess_stdout(&subprocess);

    if (!p_stdout)
    {
        throw std::runtime_error("Failed to open subprocess stdout.");
    }

    std::stringstream buffer;
    constexpr size_t bufferSize = 4096; // Adjust buffer size as necessary
    char readBuffer[bufferSize];

    // Read the output in chunks
    while (!feof(p_stdout) && !ferror(p_stdout))
    {
        size_t bytesRead = fread(readBuffer, 1, bufferSize, p_stdout);
        if (bytesRead > 0)
        {
            buffer.write(readBuffer, bytesRead);
        }
    }

    int return_code;
    subprocess_join(&subprocess, &return_code);

    if (return_code != 0)
    {
        throw std::runtime_error("Process returned non-zero exit code. Check the log for more details.");
    }

    if (ferror(p_stdout))
    {
        throw std::runtime_error("Error reading subprocess output.");
    }

    return buffer.str();
}

// static std::thread nativeStartProcessAsyncReadLine(const std::vector<std::string> *command_line, Velopack::ProcessReadLineHandler *handler)
// {
//     subprocess_s subprocess = nativeStartProcess(command_line, subprocess_option_no_window | subprocess_option_enable_async);

//     std::thread outputThread([subprocess, handler]() mutable
//     {
//         const unsigned BUFFER_SIZE = 1024;
//         char readBuffer[BUFFER_SIZE];
//         std::string accumulatedData;

//         // read all stdout from the process one line at a time
//         while (true) {
//             unsigned bytesRead = subprocess_read_stdout(&subprocess, readBuffer, BUFFER_SIZE - 1);

//             if (bytesRead == 0) {
//                 // bytesRead is 0, indicating the process has completed
//                 // Process any remaining data in accumulatedData as the last line if needed
//                 if (!accumulatedData.empty()) {
//                     handler->handleProcessOutputLine(accumulatedData);
//                 }
//                 return;
//             }

//             accumulatedData += std::string(readBuffer, bytesRead);

//             // Process accumulated data for lines
//             size_t pos;
//             while ((pos = accumulatedData.find('\n')) != std::string::npos) {
//                 std::string line = accumulatedData.substr(0, pos);
//                 if (handler->handleProcessOutputLine(line)) {
//                     return; // complete or err
//                 }
//                 accumulatedData.erase(0, pos + 1);
//             }
//         }
//     });

//     return outputThread;
// }

namespace Velopack
{
#if UNICODE
    void startup(wchar_t **args, size_t c_args)
    {
        for (size_t i = 0; i < c_args; ++i)
        {
            if (::std::wstring(args[i]) == L"--veloapp-install")
            {
                exit(0);
            }
            if (::std::wstring(args[i]) == L"--veloapp-updated")
            {
                exit(0);
            }
            if (::std::wstring(args[i]) == L"--veloapp-obsolete")
            {
                exit(0);
            }
            if (::std::wstring(args[i]) == L"--veloapp-uninstall")
            {
                exit(0);
            }
        }
    }
#endif // UNICODE

    void startup(char **args, size_t c_args)
    {
        for (size_t i = 0; i < c_args; ++i)
        {
            if (::std::string(args[i]) == "--veloapp-install")
            {
                exit(0);
            }
            if (::std::string(args[i]) == "--veloapp-updated")
            {
                exit(0);
            }
            if (::std::string(args[i]) == "--veloapp-obsolete")
            {
                exit(0);
            }
            if (::std::string(args[i]) == "--veloapp-uninstall")
            {
                exit(0);
            }
        }
    }
} // namespace Velopack

#include <algorithm>
#include <cstdlib>
#include <format>
#include <regex>
#include <stdexcept>
#include "Velopack.hpp"

namespace Velopack
{

JsonNodeType JsonNode::getKind() const
{
    return this->type;
}

bool JsonNode::isNull() const
{
    return this->type == JsonNodeType::null;
}

bool JsonNode::isEmpty() const
{
    return this->type == JsonNodeType::null || (this->type == JsonNodeType::string && this->stringValue.empty()) || (this->type == JsonNodeType::array && std::ssize(this->arrayValue) == 0) || (this->type == JsonNodeType::object && std::ssize(this->objectValue) == 0);
}

const std::unordered_map<std::string, std::shared_ptr<JsonNode>> * JsonNode::asObject() const
{
    if (this->type != JsonNodeType::object) {
        throw std::runtime_error("Cannot call AsObject on JsonNode which is not an object.");
    }
    return &this->objectValue;
}

const std::vector<std::shared_ptr<JsonNode>> * JsonNode::asArray() const
{
    if (this->type != JsonNodeType::array) {
        throw std::runtime_error("Cannot call AsArray on JsonNode which is not an array.");
    }
    return &this->arrayValue;
}

double JsonNode::asNumber() const
{
    if (this->type != JsonNodeType::number) {
        throw std::runtime_error("Cannot call AsNumber on JsonNode which is not a number.");
    }
    return this->numberValue;
}

bool JsonNode::asBool() const
{
    if (this->type != JsonNodeType::bool_) {
        throw std::runtime_error("Cannot call AsBool on JsonNode which is not a boolean.");
    }
    return this->boolValue;
}

std::string_view JsonNode::asString() const
{
    if (this->type != JsonNodeType::string) {
        throw std::runtime_error("Cannot call AsString on JsonNode which is not a string.");
    }
    return this->stringValue;
}

std::shared_ptr<JsonNode> JsonNode::parse(std::string_view text)
{
    std::shared_ptr<JsonParser> parser = std::make_shared<JsonParser>();
    parser->load(text);
    return parser->parseValue();
}

void JsonNode::initBool(bool value)
{
    if (this->type != JsonNodeType::null) {
        throw std::runtime_error("Cannot call InitBool on JsonNode which is not null.");
    }
    this->type = JsonNodeType::bool_;
    this->boolValue = value;
}

void JsonNode::initArray()
{
    if (this->type != JsonNodeType::null) {
        throw std::runtime_error("Cannot call InitArray on JsonNode which is not null.");
    }
    this->type = JsonNodeType::array;
}

void JsonNode::addArrayChild(std::shared_ptr<JsonNode> child)
{
    if (this->type != JsonNodeType::array) {
        throw std::runtime_error("Cannot call AddArrayChild on JsonNode which is not an array.");
    }
    this->arrayValue.push_back(child);
}

void JsonNode::initObject()
{
    if (this->type != JsonNodeType::null) {
        throw std::runtime_error("Cannot call InitObject on JsonNode which is not null.");
    }
    this->type = JsonNodeType::object;
}

void JsonNode::addObjectChild(std::string_view key, std::shared_ptr<JsonNode> child)
{
    if (this->type != JsonNodeType::object) {
        throw std::runtime_error("Cannot call AddObjectChild on JsonNode which is not an object.");
    }
    this->objectValue[std::string(key)] = child;
}

void JsonNode::initNumber(double value)
{
    if (this->type != JsonNodeType::null) {
        throw std::runtime_error("Cannot call InitNumber on JsonNode which is not null.");
    }
    this->type = JsonNodeType::number;
    this->numberValue = value;
}

void JsonNode::initString(std::string_view value)
{
    if (this->type != JsonNodeType::null) {
        throw std::runtime_error("Cannot call InitString on JsonNode which is not null.");
    }
    this->type = JsonNodeType::string;
    this->stringValue = value;
}

void JsonParser::load(std::string_view textInput)
{
    this->text = textInput;
    this->position = 0;
}


bool JsonParser::endReached() const
{
    return this->position >= std::ssize(this->text);
}

std::string JsonParser::readN(int n)
{
    if (this->position + n > std::ssize(this->text)) {
        throw std::runtime_error("Unexpected end of input");
    }
    std::string result{this->text.substr(this->position, n)};
    this->position += n;
    return result;
}

int JsonParser::read()
{
    if (this->position >= std::ssize(this->text)) {
        return -1;
    }
    int c = this->text[this->position];
    this->position++;
    return c;
}

int JsonParser::peek() const
{
    if (this->position >= std::ssize(this->text)) {
        return -1;
    }
    return this->text[this->position];
}

bool JsonParser::peekWhitespace() const
{
    int c = peek();
    return c == ' ' || c == '\t' || c == '\n' || c == '\r';
}

bool JsonParser::peekWordbreak() const
{
    int c = peek();
    return c == ' ' || c == ',' || c == ':' || c == '"' || c == '{' || c == '}' || c == '[' || c == ']' || c == '\t' || c == '\n' || c == '\r' || c == '/';
}

JsonToken JsonParser::peekToken()
{
    eatWhitespace();
    if (endReached())
        return JsonToken::none;
    switch (peek()) {
    case '{':
        return JsonToken::curlyOpen;
    case '}':
        return JsonToken::curlyClose;
    case '[':
        return JsonToken::squareOpen;
    case ']':
        return JsonToken::squareClose;
    case ',':
        return JsonToken::comma;
    case '"':
        return JsonToken::string;
    case ':':
        return JsonToken::colon;
    case '0':
    case '1':
    case '2':
    case '3':
    case '4':
    case '5':
    case '6':
    case '7':
    case '8':
    case '9':
    case '-':
        return JsonToken::number;
    case 't':
    case 'f':
        return JsonToken::bool_;
    case 'n':
        return JsonToken::null;
    case '/':
        read();
        if (peek() == '/') {
            while (!endReached() && peek() != '\n') {
                read();
            }
            return peekToken();
        }
        else if (peek() == '*') {
            read();
            while (!endReached()) {
                if (read() == '*' && peek() == '/') {
                    read();
                    return peekToken();
                }
            }
        }
        return JsonToken::none;
    default:
        return JsonToken::none;
    }
}

void JsonParser::eatWhitespace()
{
    while (!endReached() && peekWhitespace()) {
        read();
    }
}

std::string JsonParser::readWord()
{
    this->builder.clear();
    while (!endReached() && !peekWordbreak()) {
        this->builder.writeChar(read());
    }
    return this->builder.toString();
}

std::shared_ptr<JsonNode> JsonParser::parseNull()
{
    readWord();
    std::shared_ptr<JsonNode> node = std::make_shared<JsonNode>();
    return node;
}

std::shared_ptr<JsonNode> JsonParser::parseBool()
{
    std::string boolValue{readWord()};
    if (boolValue == "true") {
        std::shared_ptr<JsonNode> node = std::make_shared<JsonNode>();
        node->initBool(true);
        return node;
    }
    else if (boolValue == "false") {
        std::shared_ptr<JsonNode> node = std::make_shared<JsonNode>();
        node->initBool(false);
        return node;
    }
    else {
        throw std::runtime_error("Invalid boolean");
    }
}

std::shared_ptr<JsonNode> JsonParser::parseNumber()
{
    std::shared_ptr<JsonNode> node = std::make_shared<JsonNode>();
    node->initNumber(Platform::parseDouble(readWord()));
    return node;
}

std::shared_ptr<JsonNode> JsonParser::parseString()
{
    this->builder.clear();
    read();
    while (true) {
        if (endReached()) {
            throw std::runtime_error("Unterminated string");
        }
        int c = read();
        switch (c) {
        case '"':
            {
                std::shared_ptr<JsonNode> node = std::make_shared<JsonNode>();
                node->initString(this->builder.toString());
                return node;
            }
        case '\\':
            if (endReached()) {
                throw std::runtime_error("Unterminated string");
            }
            c = read();
            switch (c) {
            case '"':
            case '\\':
            case '/':
                this->builder.writeChar(c);
                break;
            case 'b':
                this->builder.writeChar(8);
                break;
            case 'f':
                this->builder.writeChar(12);
                break;
            case 'n':
                this->builder.writeChar('\n');
                break;
            case 'r':
                this->builder.writeChar('\r');
                break;
            case 't':
                this->builder.writeChar('\t');
                break;
            case 'u':
                this->builder.writeChar(Platform::parseHex(readN(4)));
                break;
            }
            break;
        default:
            this->builder.writeChar(c);
            break;
        }
    }
}

std::shared_ptr<JsonNode> JsonParser::parseObject()
{
    read();
    std::shared_ptr<JsonNode> node = std::make_shared<JsonNode>();
    node->initObject();
    while (true) {
        switch (peekToken()) {
        case JsonToken::none:
            throw std::runtime_error("Unterminated object");
        case JsonToken::comma:
            read();
            continue;
        case JsonToken::curlyClose:
            read();
            return node;
        default:
            {
                std::shared_ptr<JsonNode> name = parseString();
                if (peekToken() != JsonToken::colon)
                    throw std::runtime_error("Expected colon");
                read();
                node->addObjectChild(name->asString(), parseValue());
                break;
            }
        }
    }
}

std::shared_ptr<JsonNode> JsonParser::parseArray()
{
    read();
    std::shared_ptr<JsonNode> node = std::make_shared<JsonNode>();
    node->initArray();
    bool expectComma = false;
    while (true) {
        switch (peekToken()) {
        case JsonToken::none:
            throw std::runtime_error("Unterminated array");
        case JsonToken::comma:
            if (!expectComma) {
                throw std::runtime_error("Unexpected comma in array");
            }
            expectComma = false;
            read();
            continue;
        case JsonToken::squareClose:
            read();
            return node;
        default:
            if (expectComma) {
                throw std::runtime_error("Expected comma");
            }
            expectComma = true;
            node->addArrayChild(parseValue());
            break;
        }
    }
}

std::shared_ptr<JsonNode> JsonParser::parseValue()
{
    switch (peekToken()) {
    case JsonToken::string:
        return parseString();
    case JsonToken::number:
        return parseNumber();
    case JsonToken::bool_:
        return parseBool();
    case JsonToken::null:
        return parseNull();
    case JsonToken::curlyOpen:
        return parseObject();
    case JsonToken::squareOpen:
        return parseArray();
    default:
        throw std::runtime_error("Invalid token");
    }
}

std::string Platform::startProcessBlocking(const std::vector<std::string> * command_line)
{
    if (std::ssize(*command_line) == 0) {
        throw std::runtime_error("Command line is empty");
    }
    std::string ret{""};
     ret = nativeStartProcessBlocking(command_line); return strTrim(ret);
}

void Platform::startProcessFireAndForget(const std::vector<std::string> * command_line)
{
    if (std::ssize(*command_line) == 0) {
        throw std::runtime_error("Command line is empty");
    }
     nativeStartProcessFireAndForget(command_line); }

int Platform::getCurrentProcessId()
{
    int ret = 0;
     ret = nativeCurrentProcessId(); return ret;
}

std::string Platform::getCurrentProcessPath()
{
    std::string ret{""};
     ret = nativeGetCurrentProcessPath(); return ret;
}

bool Platform::fileExists(std::string path)
{
    bool ret = false;
     ret = nativeDoesFileExist(path); return ret;
}

bool Platform::isInstalled()
{
    return fileExists(impl_GetFusionExePath()) && fileExists(impl_GetUpdateExePath());
}

std::string Platform::getFusionExePath()
{
    std::string path{impl_GetFusionExePath()};
    if (!fileExists(path)) {
        throw std::runtime_error("Is the app installed? Fusion is not at: " + path);
    }
    return path;
}

std::string Platform::getUpdateExePath()
{
    std::string path{impl_GetUpdateExePath()};
    if (!fileExists(path)) {
        throw std::runtime_error("Is the app installed? Update is not at: " + path);
    }
    return path;
}

std::string Platform::impl_GetFusionExePath()
{
    std::string exeName{""};
    if (isWindows()) {
        exeName = "Vfusion.exe";
    }
    else if (isLinux()) {
        exeName = "VfusionNix";
    }
    else if (isOsx()) {
        exeName = "VfusionMac";
    }
    else {
        std::abort(); // "Unsupported OS"
    }
    return pathJoin(pathParent(getCurrentProcessPath()), exeName);
}

std::string Platform::impl_GetUpdateExePath()
{
    std::string exePath{getCurrentProcessPath()};
    if (isWindows()) {
        exePath = pathJoin(pathParent(pathParent(exePath)), "Update.exe");
    }
    else if (isLinux()) {
        exePath = pathJoin(pathParent(exePath), "UpdateNix");
    }
    else if (isOsx()) {
        exePath = pathJoin(pathParent(exePath), "UpdateMac");
    }
    else {
        std::abort(); // "Unsupported OS"
    }
    return exePath;
}

std::string Platform::strTrim(std::string str)
{
    std::cmatch match;
    if (std::regex_search(str.c_str(), match, std::regex("(\\S.*\\S|\\S)"))) {
        return match.str(1);
    }
    return str;
}

double Platform::parseDouble(std::string_view str)
{
    double d = 0;
    if ([&] { char *ciend; d = std::strtod(str.data(), &ciend); return *ciend == '\0'; }()) {
        return d;
    }
    throw std::runtime_error("ParseDouble failed, string is not a valid double");
}

std::string Platform::toLower(std::string_view str)
{
    std::string result{""};
     result = VeloString_ToLower(str); return result;
}

std::string Platform::toUpper(std::string_view str)
{
    std::string result{""};
     result = VeloString_ToUpper(str); return result;
}

int Platform::parseHex(std::string_view str)
{
    int i = 0;
    if ([&] { char *ciend; i = std::strtol(str.data(), &ciend, 16); return *ciend == '\0'; }()) {
        return i;
    }
    throw std::runtime_error("ParseHex failed, string is not a valid hexidecimal number");
}

std::string Platform::pathParent(std::string str)
{
    int ix_win = static_cast<int>(str.rfind('\\'));
    int ix_nix = static_cast<int>(str.rfind('/'));
    int ix = (std::max)(ix_win, ix_nix);
    return str.substr(0, ix);
}

std::string Platform::pathJoin(std::string s1, std::string s2)
{
    while (s1.ends_with('/') || s1.ends_with('\\')) {
        s1.resize(std::ssize(s1) - 1);
    }
    while (s2.starts_with('/') || s2.starts_with('\\')) {
        s2 = s2.substr(1);
    }
    return s1 + std::string(pathSeparator()) + s2;
}

std::string_view Platform::pathSeparator()
{
    if (isWindows()) {
        return "\\";
    }
    else {
        return "/";
    }
}

bool Platform::isWindows()
{
    return getOsName() == "win32";
}

bool Platform::isLinux()
{
    return getOsName() == "linux";
}

bool Platform::isOsx()
{
    return getOsName() == "darwin";
}

std::string Platform::getOsName()
{
    std::string ret{""};
     ret = nativeCurrentOsName(); return ret;
}

void Platform::exit(int code)
{
     nativeExitProcess(code); }

void StringStream::clear()
{
    this->builder.str(std::string());
}

void StringStream::write(std::string s)
{
    init();
    *this->writer << s;
}

void StringStream::writeLine(std::string s)
{
    init();
    write(s);
    writeChar('\n');
}

void StringStream::writeChar(int c)
{
    init();
    *this->writer << static_cast<char>(c);
}

std::string StringStream::toString() const
{
    return std::string(this->builder.str());
}

void StringStream::init()
{
    if (!this->initialised) {
        this->writer = &this->builder;
        this->initialised = true;
    }
}

std::shared_ptr<VelopackAsset> VelopackAsset::fromJson(std::string_view json)
{
    std::shared_ptr<JsonNode> node = JsonNode::parse(json);
    return fromNode(node);
}

std::shared_ptr<VelopackAsset> VelopackAsset::fromNode(std::shared_ptr<JsonNode> node)
{
    std::shared_ptr<VelopackAsset> asset = std::make_shared<VelopackAsset>();
    for (const auto &[k, v] : *node->asObject()) {
        if (Platform::toLower(k) == "id")
            asset->packageId = v->asString();
        else if (Platform::toLower(k) == "version")
            asset->version = v->asString();
        else if (Platform::toLower(k) == "type")
            asset->type = Platform::toLower(v->asString()) == "full" ? VelopackAssetType::full : VelopackAssetType::delta;
        else if (Platform::toLower(k) == "filename")
            asset->fileName = v->asString();
        else if (Platform::toLower(k) == "sha1")
            asset->sha1 = v->asString();
        else if (Platform::toLower(k) == "size")
            asset->size = static_cast<int64_t>(v->asNumber());
        else if (Platform::toLower(k) == "markdown")
            asset->notesMarkdown = v->asString();
        else if (Platform::toLower(k) == "html")
            asset->notesHTML = v->asString();
    }
    return asset;
}

std::shared_ptr<UpdateInfo> UpdateInfo::fromJson(std::string_view json)
{
    std::shared_ptr<JsonNode> node = JsonNode::parse(json);
    std::shared_ptr<UpdateInfo> updateInfo = std::make_shared<UpdateInfo>();
    for (const auto &[k, v] : *node->asObject()) {
        if (Platform::toLower(k) == "targetfullrelease")
            updateInfo->targetFullRelease = VelopackAsset::fromNode(v);
        else if (Platform::toLower(k) == "isdowngrade")
            updateInfo->isDowngrade = v->asBool();
    }
    return updateInfo;
}

void UpdateManagerSync::setUrlOrPath(std::string urlOrPath)
{
    this->_urlOrPath = urlOrPath;
}

void UpdateManagerSync::setAllowDowngrade(bool allowDowngrade)
{
    this->_allowDowngrade = allowDowngrade;
}

void UpdateManagerSync::setExplicitChannel(std::string explicitChannel)
{
    this->_explicitChannel = explicitChannel;
}

std::vector<std::string> UpdateManagerSync::getCurrentVersionCommand() const
{
    std::vector<std::string> command;
    command.push_back(Platform::getFusionExePath());
    command.push_back("get-version");
    return command;
}

std::vector<std::string> UpdateManagerSync::getCheckForUpdatesCommand() const
{
    if (this->_urlOrPath.empty()) {
        throw std::runtime_error("Please call SetUrlOrPath before trying to check for updates.");
    }
    std::vector<std::string> command;
    command.push_back(Platform::getFusionExePath());
    command.push_back("check");
    command.push_back("--url");
    command.push_back(this->_urlOrPath);
    if (this->_allowDowngrade) {
        command.push_back("--downgrade");
    }
    if (!this->_explicitChannel.empty()) {
        command.push_back("--channel");
        command.push_back(this->_explicitChannel);
    }
    return command;
}

std::vector<std::string> UpdateManagerSync::getDownloadUpdatesCommand(const VelopackAsset * toDownload) const
{
    if (this->_urlOrPath.empty()) {
        throw std::runtime_error("Please call SetUrlOrPath before trying to download updates.");
    }
    std::vector<std::string> command;
    command.push_back(Platform::getFusionExePath());
    command.push_back("download");
    command.push_back("--url");
    command.push_back(this->_urlOrPath);
    command.push_back("--name");
    command.push_back(toDownload->fileName);
    if (!this->_explicitChannel.empty()) {
        command.push_back("--channel");
        command.push_back(this->_explicitChannel);
    }
    return command;
}

std::vector<std::string> UpdateManagerSync::getUpdateApplyCommand(const VelopackAsset * toApply, bool silent, bool restart, bool wait, const std::vector<std::string> * restartArgs) const
{
    std::vector<std::string> command;
    command.push_back(Platform::getUpdateExePath());
    command.push_back("apply");
    if (silent) {
        command.push_back("--silent");
    }
    if (wait) {
        command.push_back("--waitPid");
        command.push_back(std::to_string( Platform::getCurrentProcessId()));
    }
    if (toApply != nullptr) {
        std::string packagesDir{getPackagesDir()};
        std::string assetPath{Platform::pathJoin(packagesDir, toApply->fileName)};
        command.push_back("--package");
        command.push_back(assetPath);
    }
    if (restart) {
        command.push_back("--restart");
    }
    if (restart && restartArgs != nullptr && std::ssize(*restartArgs) > 0) {
        command.push_back("--");
        command.insert(command.end(), restartArgs->begin(), restartArgs->end());
    }
    return command;
}

std::string UpdateManagerSync::getPackagesDir() const
{
    std::vector<std::string> command;
    command.push_back(Platform::getFusionExePath());
    command.push_back("get-packages");
    return Platform::startProcessBlocking(&command);
}

bool UpdateManagerSync::isInstalled() const
{
    return Platform::isInstalled();
}

std::string UpdateManagerSync::getCurrentVersion() const
{
    std::vector<std::string> command = getCurrentVersionCommand();
    return Platform::startProcessBlocking(&command);
}

std::shared_ptr<UpdateInfo> UpdateManagerSync::checkForUpdates() const
{
    std::vector<std::string> command = getCheckForUpdatesCommand();
    std::string output{Platform::startProcessBlocking(&command)};
    if (output.empty() || output == "null") {
        return nullptr;
    }
    return UpdateInfo::fromJson(output);
}

void UpdateManagerSync::downloadUpdates(const VelopackAsset * toDownload) const
{
    std::vector<std::string> command = getDownloadUpdatesCommand(toDownload);
    Platform::startProcessBlocking(&command);
}

void UpdateManagerSync::applyUpdatesAndExit(const VelopackAsset * toApply) const
{
    std::vector<std::string> command = getUpdateApplyCommand(toApply, false, false, false);
    Platform::startProcessFireAndForget(&command);
    Platform::exit(0);
}

void UpdateManagerSync::applyUpdatesAndRestart(const VelopackAsset * toApply, const std::vector<std::string> * restartArgs) const
{
    std::vector<std::string> command = getUpdateApplyCommand(toApply, false, true, false, restartArgs);
    Platform::startProcessFireAndForget(&command);
    Platform::exit(0);
}

void UpdateManagerSync::waitExitThenApplyUpdates(const VelopackAsset * toApply, bool silent, bool restart, const std::vector<std::string> * restartArgs) const
{
    std::vector<std::string> command = getUpdateApplyCommand(toApply, silent, restart, true, restartArgs);
    Platform::startProcessFireAndForget(&command);
}
}