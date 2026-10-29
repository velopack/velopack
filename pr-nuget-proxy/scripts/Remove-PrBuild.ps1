#Requires -Version 7.2
[CmdletBinding()]
param([string] $StateDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$StateDirectory = Get-StateDirectory $StateDirectory
$statePath = Join-Path $StateDirectory 'install.json'
if (!(Test-Path -LiteralPath $statePath)) {
    Write-Host 'No managed installation to clean up.'
    return
}
$lock = [IO.File]::Open((Join-Path $StateDirectory '.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$oldPackages = $env:NUGET_PACKAGES
$oldHttpCache = $env:NUGET_HTTP_CACHE_PATH
try {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json -AsHashtable
    $env:NUGET_PACKAGES = Join-Path $StateDirectory 'package-cache'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $StateDirectory 'http-cache'
    Restore-Install $state $StateDirectory
    Write-Host 'Restored the original vpk installation and removed the managed NuGet source.'
    Write-Host 'Stop the proxy with Ctrl+C in its terminal. Its artifact cache is retained for reuse.'
} finally {
    $env:NUGET_PACKAGES = $oldPackages
    $env:NUGET_HTTP_CACHE_PATH = $oldHttpCache
    $lock.Dispose()
}
