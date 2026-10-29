#Requires -Version 7.2
[CmdletBinding()]
param(
    [int] $PullRequest,
    [string] $Version,
    [switch] $SourceOnly,
    [string] $ProxyUrl = 'http://localhost:55631',
    [string] $NuGetConfig,
    [string] $StateDirectory,
    [string] $ToolPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$uri = [uri] $ProxyUrl
if ($uri.Scheme -notin @('http', 'https') -or !$uri.IsLoopback -or $uri.UserInfo -or $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment) {
    throw 'ProxyUrl must be an HTTP(S) loopback address.'
}
$ProxyUrl = $ProxyUrl.TrimEnd('/')
$null = Invoke-RestMethod "$ProxyUrl/health"
$builds = Invoke-RestMethod "$ProxyUrl/builds"
if (!$SourceOnly) {
    if ($Version) { $build = $builds | Where-Object version -EQ $Version | Select-Object -First 1 }
    elseif ($PullRequest -gt 0) {
        $build = $builds | Where-Object pullRequest -EQ $PullRequest |
            Sort-Object runId, attempt, { $_.packages.id } -Descending | Select-Object -First 1
    } else { throw 'Specify -PullRequest, -Version, or -SourceOnly.' }
    if (!$build) { throw 'No matching build. Check the proxy configuration and available successful PR runs.' }
    $Version = $build.version
}
if (!$NuGetConfig) {
    $NuGetConfig = if ($IsWindows) { Join-Path $env:APPDATA 'NuGet/NuGet.Config' }
        else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/NuGet/NuGet.Config' }
}
$NuGetConfig = [IO.Path]::GetFullPath($NuGetConfig)
if ($ToolPath) { $ToolPath = [IO.Path]::GetFullPath($ToolPath) }
$StateDirectory = Get-StateDirectory $StateDirectory
$null = New-Item -ItemType Directory -Force -Path $StateDirectory
$lock = [IO.File]::Open((Join-Path $StateDirectory '.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$oldPackages = $env:NUGET_PACKAGES
$oldHttpCache = $env:NUGET_HTTP_CACHE_PATH
try {
    $statePath = Join-Path $StateDirectory 'install.json'
    $sourceName = 'pr-nuget-proxy'
    $sourceUrl = "$ProxyUrl/v3/index.json"
    $document = Read-NuGetConfig $NuGetConfig
    $entry = Get-SourceEntry $document $sourceName
    if (Test-Path -LiteralPath $statePath) {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json -AsHashtable
        if ($state.NuGetConfig -ne $NuGetConfig -or $state.ToolPath -ne $ToolPath -or $state.SourceUrl -ne $sourceUrl) {
            throw 'Existing installation uses different paths or proxy URL. Clean it up before changing them.'
        }
        if (!$entry -or $entry.OuterXml -cne $state.SourceXml) { throw 'Managed source was changed. Refusing to overwrite it.' }
        $current = Get-ToolVersion $ToolPath
        if ($state.ToolTouched -and $current -and $current -notin $state.ManagedVersions -and $current -ne $state.OriginalVersion) {
            throw 'vpk was changed independently. Clean up explicitly before installing another build.'
        }
    } else {
        if ($entry) { throw "Source '$sourceName' already exists and is not owned by this helper." }
        $sources = $document.SelectSingleNode('/configuration/packageSources')
        if (!$sources) {
            $sources = $document.CreateElement('packageSources')
            $null = $document.DocumentElement.AppendChild($sources)
        }
        $entry = $document.CreateElement('add')
        $entry.SetAttribute('key', $sourceName)
        $entry.SetAttribute('value', $sourceUrl)
        $entry.SetAttribute('allowInsecureConnections', 'true')
        $null = $sources.AppendChild($entry)
        $state = @{
            NuGetConfig = $NuGetConfig; ConfigCreated = !(Test-Path -LiteralPath $NuGetConfig)
            SourceName = $sourceName; SourceUrl = $sourceUrl; SourceXml = $entry.OuterXml
            SourceTouched = $true; ToolPath = $ToolPath; ToolTouched = $false
            OriginalVersion = $null; TargetVersion = $null; ManagedVersions = @()
        }
        Save-State $state $StateDirectory
        Save-NuGetConfig $document $NuGetConfig
    }
    if (!$SourceOnly) {
        # Fetch and verify the selected package before touching an existing global tool.
        $null = Invoke-WebRequest "$ProxyUrl/flat/vpk/$Version/vpk.nuspec" -TimeoutSec 600
        $current = Get-ToolVersion $ToolPath
        if (!$state.ToolTouched) {
            $state.OriginalVersion = $current
            if ($current) {
                $packageRoot = Join-Path (Get-ToolRoot $ToolPath) ".store/vpk/$current"
                $backupPackage = Get-ChildItem -LiteralPath $packageRoot -Recurse -Filter "vpk.$current.nupkg" |
                    Select-Object -First 1
                if (!$backupPackage) { throw "Cannot back up installed vpk $current. Tool was not changed." }
                $backup = Join-Path $StateDirectory 'backup'
                $null = New-Item -ItemType Directory -Force -Path $backup
                Copy-Item -LiteralPath $backupPackage.FullName -Destination $backup
            }
        }
        $state.ToolTouched = $true
        $state.TargetVersion = $Version
        $state.ManagedVersions = @($state.ManagedVersions + $Version | Select-Object -Unique)
        Save-State $state $StateDirectory
        $env:NUGET_PACKAGES = Join-Path $StateDirectory 'package-cache'
        $env:NUGET_HTTP_CACHE_PATH = Join-Path $StateDirectory 'http-cache'
        $toolConfig = Join-Path $StateDirectory 'tool.config'
        Write-InstallConfig $toolConfig $sourceUrl
        $scope = Get-ToolScope $ToolPath
        if ($current -ne $Version) {
            if ($current) { Invoke-DotNet (@('tool', 'uninstall', 'vpk') + $scope) }
            Invoke-DotNet (@('tool', 'install', 'vpk', '--version', $Version, '--configfile', $toolConfig) + $scope)
        }
        Write-Host "Installed vpk $Version."
    }
    Write-Host "Source: $sourceUrl in $NuGetConfig"
    Write-Host 'Available Velopack versions:'
    $builds | Select-Object pullRequest, runId, version | Format-Table | Out-Host
    Write-Host "Cleanup: & '$PSScriptRoot/Remove-PrBuild.ps1' -StateDirectory '$StateDirectory'"
} catch {
    Write-Error -ErrorRecord $_ -ErrorAction Continue
    if (Test-Path -LiteralPath (Join-Path $StateDirectory 'install.json')) {
        Write-Warning 'Installation failed. Recovery state retained; run Remove-PrBuild.ps1 to restore the original state.'
    }
    throw
} finally {
    $env:NUGET_PACKAGES = $oldPackages
    $env:NUGET_HTTP_CACHE_PATH = $oldHttpCache
    $lock.Dispose()
}
