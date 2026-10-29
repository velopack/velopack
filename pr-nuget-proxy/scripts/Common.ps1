#Requires -Version 7.2
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DotNet {
    param([string[]] $Arguments)
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function Get-StateDirectory {
    param([string] $Path)
    if (!$Path) {
        $Path = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'pr-nuget-proxy/install'
    }
    return [IO.Path]::GetFullPath($Path)
}

function Save-State {
    param([hashtable] $State, [string] $Directory)
    $temporary = Join-Path $Directory 'install.json.tmp'
    [IO.File]::WriteAllText($temporary, ($State | ConvertTo-Json -Depth 20))
    [IO.File]::Move($temporary, (Join-Path $Directory 'install.json'), $true)
}

function Get-ToolScope {
    param([string] $ToolPath)
    if ($ToolPath) { return @('--tool-path', $ToolPath) }
    return @('--global')
}

function Get-ToolVersion {
    param([string] $ToolPath)
    $scope = Get-ToolScope $ToolPath
    $output = & dotnet tool list @scope
    if ($LASTEXITCODE -ne 0) { throw 'Unable to list installed tools.' }
    foreach ($line in $output) {
        if ($line -match '^vpk\s+(\S+)\s+') { return $Matches[1] }
    }
    return $null
}

function Get-ToolRoot {
    param([string] $ToolPath)
    if ($ToolPath) { return $ToolPath }
    $homePath = if ($env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME } else { [Environment]::GetFolderPath('UserProfile') }
    return Join-Path $homePath '.dotnet/tools'
}

function Write-InstallConfig {
    param([string] $Path, [string] $Source)
    $document = [xml] '<configuration><packageSources><clear /></packageSources></configuration>'
    $entry = $document.CreateElement('add')
    $entry.SetAttribute('key', 'pr-build')
    $entry.SetAttribute('value', $Source)
    if ($Source.StartsWith('http://', [StringComparison]::OrdinalIgnoreCase)) {
        $entry.SetAttribute('allowInsecureConnections', 'true')
    }
    $null = $document.configuration.packageSources.AppendChild($entry)
    $document.Save($Path)
}

function Read-NuGetConfig {
    param([string] $Path)
    $document = [xml]::new()
    $document.PreserveWhitespace = $true
    if (Test-Path -LiteralPath $Path) { $document.Load($Path) }
    else { $document.LoadXml('<configuration />') }
    if ($document.DocumentElement.Name -ne 'configuration') { throw "Invalid NuGet configuration: $Path" }
    return ,$document
}

function Save-NuGetConfig {
    param([xml] $Document, [string] $Path)
    $null = New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($Path))
    $temporary = "$Path.pr-proxy.tmp"
    try {
        $Document.Save($temporary)
        [IO.File]::Move($temporary, $Path, $true)
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
}

function Get-SourceEntry {
    param([xml] $Document, [string] $Name)
    return $Document.SelectNodes('/configuration/packageSources/add') |
        Where-Object { $_.GetAttribute('key') -ceq $Name } | Select-Object -First 1
}

function Restore-Install {
    param([hashtable] $State, [string] $Directory)
    if ($State.ToolTouched) {
        $current = Get-ToolVersion $State.ToolPath
        if ($current -and $current -notin $State.ManagedVersions -and $current -ne $State.OriginalVersion) {
            throw "vpk is now $current, not the helper-installed version. Refusing to replace an independently changed tool."
        }
        $scope = Get-ToolScope $State.ToolPath
        if ($current -ne $State.OriginalVersion) {
            if ($current) { Invoke-DotNet (@('tool', 'uninstall', 'vpk') + $scope) }
            if ($State.OriginalVersion) {
                $backupConfig = Join-Path $Directory 'restore.config'
                Write-InstallConfig $backupConfig (Join-Path $Directory 'backup')
                Invoke-DotNet (@('tool', 'install', 'vpk', '--version', $State.OriginalVersion, '--configfile', $backupConfig) + $scope)
            }
        }
    }
    if ($State.SourceTouched -and (Test-Path -LiteralPath $State.NuGetConfig)) {
        $document = Read-NuGetConfig $State.NuGetConfig
        $entry = Get-SourceEntry $document $State.SourceName
        if ($entry) {
            if ($entry.OuterXml -cne $State.SourceXml) {
                throw "Source '$($State.SourceName)' has been changed independently. Recovery state retained."
            }
            $null = $entry.ParentNode.RemoveChild($entry)
            $sources = $document.SelectSingleNode('/configuration/packageSources')
            if (!$sources.HasChildNodes) { $null = $sources.ParentNode.RemoveChild($sources) }
            if ($State.ConfigCreated -and !$document.DocumentElement.HasChildNodes) {
                Remove-Item -LiteralPath $State.NuGetConfig
            } else { Save-NuGetConfig $document $State.NuGetConfig }
        }
    }
    # Only remove the helper's explicitly named files/directories; never the state root itself.
    foreach ($name in @('backup', 'package-cache', 'http-cache')) {
        $path = Join-Path $Directory $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    foreach ($name in @('tool.config', 'restore.config', 'install.json', 'install.json.tmp')) {
        $path = Join-Path $Directory $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}
