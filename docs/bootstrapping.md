| [docs](.) / bootstrapping.md |
|:---|

# Bootstrapping
*Applies to: Windows*

While installing Velopack applications on Windows, it is possible to install other commonly required runtime dependencies using the `--framework` / `-f` argument.

It is possibly to specify more than one requirement, using a comma delimited list. For example:
```cmd
vpk pack ... --framework net6.0-x64-desktop,vcredist142-x64
```

These dependencies will be downloaded and installed before your application can be installed.

## Adding dependencies during updates

Velopack will check that all required dependencies are installed before applying new updates. This means if a new version of your app adds a new dependency, the user will be prompted to install it before your new version is applied.

## List of supported frameworks
Any of the following can be passed via the `--framework` argument.

### Edge WebView2
- `webview2`

### vcredist
- `vcredist100-x86` (VC++ 10.0 / VS 2010)
- `vcredist100-x64` (VC++ 10.0 / VS 2010)
- `vcredist110-x86` (VC++ 11.0 / VS 2012)
- `vcredist110-x64` (VC++ 11.0 / VS 2012)
- `vcredist120-x86` (VC++ 12.0 / VS 2013)
- `vcredist120-x64` (VC++ 12.0 / VS 2013)
- `vcredist140-x86` (VC++ 14.0 / VS 2015)
- `vcredist140-x64` (VC++ 14.0 / VS 2015)
- `vcredist141-x86` (VC++ 14.1 / VS 2017)
- `vcredist141-x64` (VC++ 14.1 / VS 2017)
- `vcredist142-x86` (VC++ 14.2 / VS 2019)
- `vcredist142-x64` (VC++ 14.2 / VS 2019)
- `vcredist143-x86` (VC++ 14.3 / VS 2022)
- `vcredist143-x64` (VC++ 14.3 / VS 2022)
- `vcredist143-arm64` (VC++ 14.3 / VS 2022)

### .Net Framework
- `net45`
- `net451`
- `net452`
- `net46`
- `net461`
- `net462`
- `net47`
- `net471`
- `net472`
- `net48`
- `net481`

### dotnet
Every version of dotnet is supported >= 5.0. The framework argument should be supplied in the format `$"net{major.minor}-{arch}-{type}"`.

The valid `{arch}` values are
- x86
- x64
- arm64

The valid `{type}` values are
- runtime
- aspnetcore
- desktop

Here are some examples:
- .NET 6.0 Desktop Runtime (x64)  `--framework net6.0-x64-desktop`
- .NET 8.0 Runtime (arm64)  `--framework net8.0-arm64-runtime`
- .NET 5.0 AspNetCore (x86)  `--framework net5.0-x86-aspnetcore`

By default, Velopack will accept any installed release, but always install the latest. That is to say, if your dependency is specified as `net6.0-x64-desktop` and version `6.0.2` is installed, it will be accepted. If it's not installed, Velopack will download the latest available version (at the time of writing, that's `6.0.26`). 

If you need a specific version of dotnet, (eg. `6.0.11`) - you can specify a third version part in your dependency string: `--framework net6.0.11-x64-desktop`. In this case, if the installed version is `< 6.0.11`, then it will be upgraded to the latest available.