| [docs](.) / cli.md |
|:---|

# Velopack Command Line Reference

## vpk
```txt
Description:
  Velopack CLI 0.0.61-g2e7ffeb (prerelease) for creating and distributing releases.

Usage:
  vpk [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information
  --verbose       Print diagnostic messages.

Commands:
  pack      Creates a release from a folder containing application files.
  download  Download's the latest release from a remote update source.
  upload    Upload local package(s) to a remote update source.
  delta     Utilities for creating or applying delta packages.
```

## Update.exe & UpdateMac
```txt
Velopack Updater (0.0.66) manages packages and installs updates.
https://github.com/velopack/velopack

Usage: update [OPTIONS]
       update apply [OPTIONS] [-- [EXE_ARGS]...]
       update patch [OPTIONS] --old <FILE> --patch <FILE> --output <FILE>
       update start [OPTIONS] [EXE_NAME] [-- [EXE_ARGS]...]
       update uninstall [OPTIONS]

Options:
      --verbose     Print debug messages to console / log
  -s, --silent      Don't show any prompts / dialogs
  -l, --log <PATH>  Override the default log file location
  -h, --help        Print help
  -V, --version     Print version

update apply:
Applies a staged / prepared update, installing prerequisite runtimes if necessary
  -r, --restart         Restart the application after the update
  -w, --wait            Wait for the parent process to terminate before applying the update
  -p, --package <FILE>  Update package to apply
      --noelevate       If the application does not have sufficient privileges, do not elevate to admin
  -h, --help            Print help
  [EXE_ARGS]...     Arguments to pass to the started executable. Must be preceeded by '--'.

update patch:
Applies a Zstd patch file
      --old <FILE>     Base / old file to apply the patch to
      --patch <FILE>   The Zstd patch to apply to the old file
      --output <FILE>  The file to create with the patch applied
  -h, --help           Print help

update start:
Starts the currently installed version of the application
  -w, --wait         Wait for the parent process to terminate before starting the application
  -h, --help         Print help
  [EXE_ARGS]...  Arguments to pass to the started executable. Must be preceeded by '--'.
  [EXE_NAME]     The optional name of the binary to execute

update uninstall:
Remove all app shortcuts, files, and registry entries.
  -h, --help  Print help
```


## Setup.exe

```txt
Velopack Setup (0.0.66) installs applications.
https://github.com/velopack/velopack

Usage: setup [OPTIONS]

Options:
  -s, --silent           Hides all dialogs and answers 'yes' to all prompts
  -v, --verbose          Print debug messages to console
  -l, --log <FILE>       Enable file logging and set location
  -t, --installto <DIR>  Installation directory to install the application
  -d, --debug <FILE>     Debug mode, install from a nupkg file
  -h, --help             Print help
```