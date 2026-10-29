#Requires -Version 7.2
[CmdletBinding()]
param(
    [string] $ProxyUrl = 'http://localhost:55631',
    [int] $PullRequest = 755
)
. (Join-Path $PSScriptRoot '../scripts/Common.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) "pr-nuget-smoke-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $root
$oldPackages = $env:NUGET_PACKAGES
$oldHttpCache = $env:NUGET_HTTP_CACHE_PATH
$env:NUGET_PACKAGES = Join-Path $root 'consumer-packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $root 'http-cache'
$toolPath = Join-Path $root 'tools'
$state = Join-Path $root 'state'
$config = Join-Path $root 'nuget.config'
$install = Join-Path $PSScriptRoot '../scripts/Install-PrBuild.ps1'
$remove = Join-Path $PSScriptRoot '../scripts/Remove-PrBuild.ps1'
try {
    $builds = Invoke-RestMethod "$ProxyUrl/builds"
    $build = $builds | Where-Object pullRequest -EQ $PullRequest |
        Sort-Object runId, attempt, { $_.packages.id } -Descending | Select-Object -First 1
    if (!$build) { throw "No available build for PR $PullRequest." }
    $version = $build.version
    $feed = "$ProxyUrl/v3/index.json"
    Write-InstallConfig $config 'https://api.nuget.org/v3/index.json'

    # Establish a real pre-existing tool whose restoration can be verified.
    $baselineDirectory = Join-Path $root 'baseline'
    $null = New-Item -ItemType Directory -Path $baselineDirectory
    $baseline = '1.3.67-smokebaseline'
    $baselinePackage = Join-Path $baselineDirectory "vpk.$baseline.nupkg"
    Invoke-WebRequest "$ProxyUrl/flat/vpk/$version/vpk.$version.nupkg" -OutFile $baselinePackage -TimeoutSec 600
    $zip = [IO.Compression.ZipFile]::Open($baselinePackage, 'Update')
    try {
        $entry = $zip.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $manifest = [xml] $reader.ReadToEnd() } finally { $reader.Dispose() }
        $manifest.package.metadata.version = $baseline
        $entryName = $entry.FullName
        $entry.Delete()
        $output = $zip.CreateEntry($entryName).Open()
        try { $manifest.Save($output) } finally { $output.Dispose() }
    } finally { $zip.Dispose() }
    $baselineConfig = Join-Path $root 'baseline.config'
    Write-InstallConfig $baselineConfig $baselineDirectory
    Invoke-DotNet @('tool', 'install', 'vpk', '--tool-path', $toolPath, '--version', $baseline, '--configfile', $baselineConfig)

    & $install -ProxyUrl $ProxyUrl -PullRequest $PullRequest -ToolPath $toolPath -StateDirectory $state -NuGetConfig $config
    if ((Get-ToolVersion $toolPath) -ne $version) { throw 'PR tool version did not match.' }
    $executable = Join-Path $toolPath $(if ($IsWindows) { 'vpk.exe' } else { 'vpk' })
    & $executable --help
    if ($LASTEXITCODE -ne 0) { throw 'Installed tool failed to execute.' }

    # Reinstall must preserve the original baseline, not adopt the PR tool as baseline.
    & $install -ProxyUrl $ProxyUrl -Version $version -ToolPath $toolPath -StateDirectory $state -NuGetConfig $config
    $document = Read-NuGetConfig $config
    $unrelated = $document.CreateElement('add')
    $unrelated.SetAttribute('key', 'unrelated')
    $unrelated.SetAttribute('value', (Join-Path $root 'unrelated-feed'))
    $null = New-Item -ItemType Directory -Path (Join-Path $root 'unrelated-feed')
    $null = $document.SelectSingleNode('/configuration/packageSources').AppendChild($unrelated)
    Save-NuGetConfig $document $config
    $null = New-Item -ItemType Directory -Path (Join-Path $root 'consumer')
    $consumer = Join-Path $root 'consumer/Consumer.csproj'
    [IO.File]::WriteAllText($consumer, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup><PackageReference Include="Velopack" Version="[$version]" /></ItemGroup>
</Project>
"@)
    [IO.File]::WriteAllText((Join-Path $root 'consumer/Program.cs'),
        'System.Console.WriteLine(typeof(Velopack.UpdateManager).Assembly.GetName().Name);')
    Invoke-DotNet @('restore', $consumer, '--configfile', $config, '--force', '--no-http-cache', '-v', 'minimal')
    Invoke-DotNet @('run', '--project', $consumer, '--no-restore', '--no-launch-profile')
    $assets = Get-Content (Join-Path $root 'consumer/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
    if (!$assets.libraries.ContainsKey("Velopack/$version")) { throw 'Consumer resolved a different package version.' }

    & $remove -StateDirectory $state
    if ((Get-ToolVersion $toolPath) -ne $baseline) { throw 'Original tool was not restored.' }
    & $executable --help | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Restored original tool failed to execute after cleanup.' }
    $document = Read-NuGetConfig $config
    if (Get-SourceEntry $document 'pr-nuget-proxy') { throw 'Managed source was not removed.' }
    if (!(Get-SourceEntry $document 'unrelated')) { throw 'Cleanup removed an unrelated source.' }
    & $remove -StateDirectory $state

    # Also cover installations where no previous vpk or NuGet configuration existed.
    Invoke-DotNet @('tool', 'uninstall', 'vpk', '--tool-path', $toolPath)
    $freshConfig = Join-Path $root 'fresh.config'
    & $install -ProxyUrl $ProxyUrl -Version $version -ToolPath $toolPath -StateDirectory $state -NuGetConfig $freshConfig
    & $remove -StateDirectory $state
    if (Get-ToolVersion $toolPath) { throw 'Cleanup left a tool that did not exist originally.' }
    if (Test-Path -LiteralPath $freshConfig) { throw 'Cleanup left a configuration it created.' }
    Write-Host 'PASS: real PR package restore, tool execution, repeat install, baseline restoration, unrelated edits, and clean removal.'
} finally {
    $env:NUGET_PACKAGES = $oldPackages
    $env:NUGET_HTTP_CACHE_PATH = $oldHttpCache
    if (Test-Path -LiteralPath (Join-Path $state 'install.json')) {
        Write-Warning "Smoke-test recovery state retained at $root. Run Remove-PrBuild.ps1 -StateDirectory '$state'."
    } else {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
