#Requires -Version 7.2
param([string] $Config)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../Proxy/Proxy.csproj'
$arguments = @('run', '--project', $project, '--no-launch-profile')
if ($Config) { $arguments += @('--', '--config', [IO.Path]::GetFullPath($Config)) }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Proxy exited with code $LASTEXITCODE." }
