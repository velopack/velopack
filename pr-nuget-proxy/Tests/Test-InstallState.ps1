#Requires -Version 7.2
. (Join-Path $PSScriptRoot '../scripts/Common.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) "pr-nuget-state-tests-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $root
$script:CurrentVersion = $null
$script:Calls = [Collections.Generic.List[string]]::new()

function Get-ToolVersion {
    param([string] $ToolPath)
    return $script:CurrentVersion
}
function Invoke-DotNet {
    param([string[]] $Arguments)
    $script:Calls.Add(($Arguments -join ' '))
    if ($Arguments[1] -eq 'uninstall') { $script:CurrentVersion = $null }
    elseif ($Arguments[1] -eq 'install') { $script:CurrentVersion = $Arguments[4] }
}
function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (!$Condition) { throw $Message }
}
function Assert-Throws {
    param([scriptblock] $Action, [string] $Expected)
    try { & $Action } catch {
        if ($_.Exception.Message.Contains($Expected)) { return }
        throw
    }
    throw "Expected error containing '$Expected'."
}

try {
    $config = Join-Path $root 'nuget.config'
    $document = [xml] '<configuration><packageSources><clear /><add key="unrelated" value="C:\feed" /></packageSources></configuration>'
    $entry = $document.CreateElement('add')
    $entry.SetAttribute('key', 'pr-nuget-proxy')
    $entry.SetAttribute('value', 'http://127.0.0.1:5741/v3/index.json')
    $entry.SetAttribute('allowInsecureConnections', 'true')
    $null = $document.configuration.packageSources.AppendChild($entry)
    Save-NuGetConfig $document $config
    $state = @{
        ToolTouched = $false; ToolPath = ''; OriginalVersion = '1.0.0'
        TargetVersion = '1.0.0-pr.2'; ManagedVersions = @('1.0.0-pr.1', '1.0.0-pr.2')
        SourceTouched = $true; NuGetConfig = $config; SourceName = 'pr-nuget-proxy'
        SourceXml = $entry.OuterXml; ConfigCreated = $false
    }
    Save-State $state $root
    Restore-Install $state $root
    $result = Read-NuGetConfig $config
    Assert-True ($null -eq (Get-SourceEntry $result 'pr-nuget-proxy')) 'Managed source remained.'
    Assert-True ($null -ne (Get-SourceEntry $result 'unrelated')) 'Unrelated source was lost.'
    Assert-True ($null -ne $result.SelectSingleNode('/configuration/packageSources/clear')) 'Clear directive was lost.'
    Assert-True ($script:Calls.Count -eq 0) 'Source-only cleanup touched a tool.'

    $state.SourceTouched = $false
    $state.ToolTouched = $true
    $script:CurrentVersion = '9.9.9-independent'
    Save-State $state $root
    Assert-Throws { Restore-Install $state $root } 'independently changed tool'
    Assert-True (Test-Path -LiteralPath (Join-Path $root 'install.json')) 'Conflict discarded recovery state.'
    Assert-True ($script:Calls.Count -eq 0) 'Conflict changed a tool.'

    # Interrupted switching from PR 1 to PR 2 must still allow restoration of the original.
    $script:CurrentVersion = '1.0.0-pr.1'
    Restore-Install $state $root
    Assert-True ($script:CurrentVersion -eq '1.0.0') 'Interrupted switch failed to restore the original.'
    Assert-True ($script:Calls.Count -eq 2) 'Restoration did not uninstall/install exactly once.'

    # A failed install after uninstalling the original must also be recoverable.
    $script:Calls.Clear()
    $script:CurrentVersion = $null
    Restore-Install $state $root
    Assert-True ($script:CurrentVersion -eq '1.0.0') 'Failed install did not restore missing original tool.'
    Assert-True ($script:Calls.Count -eq 1) 'Recovery attempted unnecessary uninstall.'

    $state.SourceTouched = $true
    $state.ToolTouched = $false
    $document = Read-NuGetConfig $config
    $changed = $document.ImportNode($entry, $true)
    $changed.SetAttribute('value', 'http://127.0.0.1:9999/v3/index.json')
    $null = $document.SelectSingleNode('/configuration/packageSources').AppendChild($changed)
    Save-NuGetConfig $document $config
    Save-State $state $root
    Assert-Throws { Restore-Install $state $root } 'changed independently'
    Assert-True (Test-Path -LiteralPath (Join-Path $root 'install.json')) 'Source conflict discarded recovery state.'
    Assert-True ((Get-SourceEntry (Read-NuGetConfig $config) 'pr-nuget-proxy').GetAttribute('value').Contains('9999')) `
        'Source conflict overwrote the edit.'
    Write-Host 'PASS: source ownership, unrelated configuration, interrupted installs/switches, and independent tool/source conflict handling.'
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force
}
