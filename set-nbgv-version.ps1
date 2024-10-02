$scriptDir = $PSScriptRoot
$path = Join-Path $scriptDir "Cargo.toml"
$version = (nbgv get-version -v NuGetPackageVersion).Trim()
Write-Host "Setting version to $version"

(Get-Content $path) | ForEach-Object {
    if ($_ -match '^version\s*=\s*".*"') {
        $_ -replace '^version\s*=\s*".*"', "version = `"$version`""
    }
    else {
        $_
    }
} | Set-Content $path

cargo pkgid -p velopack

Set-Location "$scriptDir/src/lib-nodejs"
npm version $version --no-git-tag-version