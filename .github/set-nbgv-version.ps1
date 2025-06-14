param(
    [string]$version = $(nbgv get-version -v NuGetPackageVersion).Trim()
)

$originalLocation = Get-Location

# setting cargo workspace version
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

# setting nodejs version
Set-Location "$scriptDir/src/lib-nodejs"
npm version $version --no-git-tag-version

# setting pyproject.toml version
$pyprojectPath = [IO.Path]::Combine($scriptDir, 'src', 'lib-python', 'pyproject.toml')

$pythonVersion = $version
if ($pythonVersion -match '-g') {
    $pythonVersion = $pythonVersion -replace '-g', '.dev+'
}

(Get-Content $pyprojectPath) | ForEach-Object {
    if ($_ -match '^version\s*=\s*".*"') {
        $_ -replace '^version\s*=\s*".*"', "version = `"$pythonVersion`""
    }
    else {
        $_
    }
} | Set-Content $pyprojectPath

# copying README.md
Copy-Item -Path "$scriptDir/README_NUGET.md" -Destination "$scriptDir/src/lib-nodejs/README.md" -Force
Copy-Item -Path "$scriptDir/README_NUGET.md" -Destination "$scriptDir/src/lib-rust/README.md" -Force

Set-Location $originalLocation

