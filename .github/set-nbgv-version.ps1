param(
    [string]$version = $(nbgv get-version -v NuGetPackageVersion).Trim()
)

$scriptDir = "$PSScriptRoot/.."
$path = Join-Path $scriptDir "Cargo.toml"
Write-Host "Setting version to $version"

(Get-Content $path) | ForEach-Object {
    if ($_ -match '^version\s*=\s*".*"') {
        $_ -replace '^version\s*=\s*".*"', "version = `"$version`""
    }
    else {
        $_
    }
} | Set-Content $path

Set-Location "$scriptDir/src/lib-nodejs"
npm version $version --no-git-tag-version

Copy-Item -Path "$scriptDir/README_NUGET.md" -Destination "$scriptDir/src/lib-nodejs/README.md" -Force
Copy-Item -Path "$scriptDir/README_NUGET.md" -Destination "$scriptDir/src/lib-rust/README.md" -Force