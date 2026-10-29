# PR NuGet Proxy

A standalone, localhost NuGet V3 feed for testing GitHub Actions PR artifacts.
It has no references to Velopack projects, build targets, versioning, signing, or
test infrastructure. Copy this directory into another repository to use it there.
The example configuration targets Velopack; repository, workflow, artifact names,
and package IDs are configurable.

## Requirements

- .NET SDK 8 or later, with the ASP.NET Core 8 runtime (or publish self-contained).
- GitHub CLI (`gh`), authenticated using `gh auth login`, or `GH_TOKEN`/`GITHUB_TOKEN`.
  The account/token needs access to Actions artifacts in the configured repository.
- PowerShell 7.2 or later for the convenience scripts.
- Installing a PR tool requires a runtime supported by that tool. Velopack's
  example artifact supports .NET 8, 9, and 10.

Only use builds you trust. PR packages contain executable code. This app does not
sandbox them, and installing a global tool makes that code available as a command.
No GitHub credentials are stored in NuGet configuration or exposed to clients.

## Start the feed

Commands below assume the current directory is `pr-nuget-proxy`.

```powershell
Copy-Item proxy.example.json proxy.local.json
# Optionally edit proxy.local.json to restrict the open PRs or pin runs.
.\scripts\Start-Proxy.ps1 -Config .\proxy.local.json
```

Keep that terminal open. The feed is `http://localhost:55631/v3/index.json`.
Stop with Ctrl+C. The default URL binds to localhost.
There is no service installation, background autostart, or firewall change.

The app uses standard ASP.NET Core hosting: `Program.cs`, `appsettings.json`,
environment-specific settings, environment variables, command-line configuration,
dependency injection, and validated options under the `Proxy` section. The
development launch profile is available in Visual Studio or `dotnet run`.
The start script uses the HTTP settings without a launch profile.

By default it queries **all open PRs** in `velopack/velopack` and discovers their
completed successful workflow runs. Optional `--config` JSON files use the same
`Proxy` section as `appsettings.json`; environment variables and command-line
settings override that file. For example, use `Proxy__Repository` for an environment
variable override. Configure `Urls` for ASP.NET Core listening addresses and
`Proxy:BaseUrl` for the public address advertised to NuGet clients; keep them in
sync. Configuration changes require a restart.

For a published application, rather than `dotnet run`:

```powershell
dotnet publish .\Proxy\Proxy.csproj -o .\out
dotnet .\out\PrNuGetProxy.dll --contentRoot .\out --config .\proxy.local.json
```

## Install a PR tool and register the source

In another PowerShell terminal:

```powershell
.\scripts\Install-PrBuild.ps1 -PullRequest 755
```

This registers a source named `pr-nuget-proxy` in your user NuGet configuration and
replaces the global `vpk` with the latest discovered successful build for that PR.
It backs up any existing global `vpk` package before uninstalling it. Repeated PR
installs preserve that original baseline, not the previously selected PR build.

Other options:

```powershell
# Register the feed without changing vpk.
.\scripts\Install-PrBuild.ps1 -SourceOnly

# Register the source in a consumer solution that clears inherited sources.
.\scripts\Install-PrBuild.ps1 -PullRequest 755 -NuGetConfig C:\MyApp\nuget.config

# Select an exact version shown by the proxy.
Invoke-RestMethod http://localhost:55631/builds
.\scripts\Install-PrBuild.ps1 -Version '1.3.67-pr.755.run.39731693529.attempt.1.artifact.12406230632'

# Isolated tool installation, instead of replacing your global tool.
.\scripts\Install-PrBuild.ps1 -PullRequest 755 -ToolPath C:\MyTools\pr-vpk `
    -StateDirectory C:\MyTools\pr-vpk-state
```

The install script supports `vpk` specifically. The feed itself supports configured
package IDs. Use one state directory per independently managed tool/configuration
pair; clean up before changing its proxy URL, configuration path, or tool scope.
Do not edit or delete the state directory until cleanup succeeds.

An isolated configuration ensures tool installation uses the proxy, not a racing
public source. Installation failures preserve recovery state and report the cleanup
command; they do not silently claim success or discard the original tool backup.

## Use the C# package

In Visual Studio, select the source and enable **Include prerelease**. Select the
desired PR/run version of `Velopack`. Alternatively, set that version in the
consumer's `PackageReference` or central package-version file and restore.
Use `[version]` in `PackageReference` when an exact version constraint is desired.

The helper does not edit project references. Adding a feed does not automatically
change an application's existing package version.

NuGet configuration with `<clear />` ignores user-level sources. In that case use
`-NuGetConfig` with the consumer's configuration, or add this entry yourself:

```xml
<add key="pr-nuget-proxy" value="http://localhost:55631/v3/index.json"
     allowInsecureConnections="true" />
```

Keep nuget.org (or your normal feeds) for ordinary dependencies: this app serves
PR artifacts only, not an upstream mirror of nuget.org. If a consumer uses Package
Source Mapping, its existing rules must also permit the selected package IDs from
this source; the helper intentionally does not modify those rules.

## Cleanup

```powershell
.\scripts\Remove-PrBuild.ps1
# Supply the same -StateDirectory if installation used a custom one.
```

Cleanup restores the original `vpk` version, or removes `vpk` if none existed.
It removes only the source it added and its installation backup/private package
cache, preserving unrelated NuGet configuration edits. Independently changed tool
versions or managed source entries cause an explicit refusal; recovery state is
retained. Cleanup is safe to repeat. It does not require the proxy or GitHub to
restore the saved tool package.

Stop the proxy with Ctrl+C separately. Its artifact cache is deliberately retained
for reuse. Once stopped, that cache can be deleted manually.
Consumer project references, lock files, and packages downloaded by your own restores
are not owned by the helper and are not rolled back. No broad NuGet cache clearing
is performed.

Default state locations:

| Data | Location |
| --- | --- |
| Proxy catalog, original artifact ZIPs, and rewritten packages | `LocalApplicationData\pr-nuget-proxy\cache` |
| Tool backup and installation journal | `LocalApplicationData\pr-nuget-proxy\install` |

`LocalApplicationData` is resolved through .NET on each platform. Set
`CacheDirectory` in configuration to override the first, and `-StateDirectory`
to override the second. Small lock files/empty state directories may remain after
cleanup. Use a private, user-owned directory, not a shared or untrusted location.

## Discovery and caching

The following settings belong under the `Proxy` section:

- `Repository`: GitHub `owner/repository`.
- `Workflow`: workflow filename, for example `build.yml`.
- `PackagesArtifact`: artifact containing `.nupkg` files, optionally in subfolders.
- `VersionArtifact`: artifact containing a root `version.txt` with the original
  package version. All configured packages must use that version.
- `PackageIds`: package allowlist. Defaults to `Velopack` and `vpk`.
- `PullRequests`: optional restriction on open PRs. An empty array considers all
  open PRs with runs in the scan.
- `RunIds`: additionally pin specific successful `pull_request` runs, including
  older runs. These must still belong to an **open** PR and be completed and
  successful. Pinning cannot make artifacts from a closed PR visible.
- `RunLimit`: scan the most recent 1–500 successful PR runs; defaults to 100.
  A PR absent from this window needs its run explicitly pinned.
- `RefreshSeconds`: absolute expiration of the `IMemoryCache` discovery entry;
  defaults to 120, minimum 10.
- `BaseUrl`: feed address advertised to NuGet, defaults to `http://localhost:55631`.
- `CacheDirectory`: absolute or working-directory-relative cache path.

The feed queries the repository's paginated open-PR list, then discovers the newest
completed successful run for each selected open PR in the scan, plus pinned runs.
Queued, running, failed, cancelled, and non-PR runs are excluded.
This is **latest successful**, not necessarily the PR's current
head: a newer build may still be running or may have failed. Existing discovered
versions of open PRs are retained as new ones appear. Closed/merged PRs disappear
from all feed endpoints on the next discovery refresh, even if their packages
remain on disk. The `/builds` endpoint shows provenance.
`IMemoryCache` controls when GitHub is checked again; concurrent requests share
one refresh. Discovery is request-driven, with no polling while idle and no custom
timestamp-based refresh logic. Failed refreshes are not cached.

Local files are always checked first. Known completed builds reuse the persisted
catalog without querying their artifact lists again. Both version artifacts and
package artifacts are kept as SHA256-verified ZIPs on disk, keyed by artifact ID.
Restarts and rebuilding rewritten packages reuse those ZIPs rather than
downloading them again. Expired artifacts can still be read if already cached.

An uncached version artifact is fetched during discovery. The larger packages
artifact is downloaded on first package/registration/search access and rewritten
once for all configured package IDs. GitHub
downloads the entire artifact ZIP, not just a requested `.nupkg`; the example is
about 145 MB. Searches requiring package metadata can therefore take time initially.
Missing artifacts and expired artifacts without local copies are skipped for
automatic discovery and fail explicitly for eligible pinned runs.
Previously discovered but uncached artifacts may subsequently
expire; downloading them will then fail. Cache packages before artifact retention
expires if you need them later.

GitHub/authentication failures fail requests and are logged in the server terminal,
not converted to empty successful feeds. A fresh GitHub check is required on cache
expiration or restart so a saved catalog cannot expose now-closed PRs. There is no
offline discovery bypass; local packages are served without downloads while the
discovery entry is valid.
The app refuses simultaneous instances sharing a cache and refuses reusing a cache
for another repository/package selection.

## Package identity

For example:

```text
Original: 1.3.67-g4f76ba3
Proxy:    1.3.67-pr.755.run.39731693529.attempt.1.artifact.12406230632
```

Package IDs stay unchanged. The feed metadata, downloadable `.nuspec`, and embedded
`.nuspec` agree on the proxy version. Including the artifact ID also distinguishes
reruns/replaced artifacts. Never publish different bytes under an existing identity.
`+build` metadata is not used because NuGet ignores it for identity.

Assemblies, native binaries, and tool settings remain unchanged; the tool's own help
still displays its original compiled version. ZIP timestamps and file attributes
are preserved. Signed NuGet packages are rejected rather than silently losing their
signature. Symbol packages are not served. This initial implementation assumes
configured packages do not depend on each other; coordinated dependency rewriting
and symbol-server support are not included.

## Development and verification

```powershell
dotnet build .\Proxy\Proxy.csproj
dotnet run --project .\Tests\Tests.csproj -- -noLogo
.\Tests\Test-InstallState.ps1

# With a running proxy and an available PR artifact:
.\Tests\Smoke-Test.ps1 -ProxyUrl http://localhost:55631 -PullRequest 755
```

Tests are independent of GitHub and cover rewriting, payload preservation, artifact
verification, catalog persistence, open-PR filtering, completed-run filtering,
memory-cache invalidation, atomic publication, concurrent requests, local artifact
reuse, NuGet protocol parsing/search, and HTTP responses.
The live smoke test uses a temporary tool path/configuration/package cache; it
restores a pre-existing tool baseline, executes the PR tool, builds a real C#
consumer, checks repeated installs and unrelated config edits, and removes a fresh
installation. It never replaces your actual global tool.

The whole directory is self-contained, including NuGet sources, SDK selection,
MSBuild boundaries, and editor settings. It is intentionally not added to the
parent repository's solution or CI.
