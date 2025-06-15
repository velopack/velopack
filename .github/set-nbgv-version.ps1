# param(
#     [string]$nuGetPackageVersion = $(nbgv get-version -v NuGetPackageVersion).Trim()
# )

$semverVersion = $(nbgv get-version -v NuGetPackageVersion).Trim()
$fourpartVersion = $(nbgv get-version -v Version).Trim()

$originalLocation = Get-Location

# setting cargo workspace version
$scriptDir = "$PSScriptRoot/.."
$path = Join-Path $scriptDir "Cargo.toml"
Write-Host "Setting version to $semverVersion"

(Get-Content $path) | ForEach-Object {
    if ($_ -match '^version\s*=\s*".*"') {
        $_ -replace '^version\s*=\s*".*"', "version = `"$semverVersion`""
    }
    else {
        $_
    }
} | Set-Content $path

# setting nodejs version
Set-Location "$scriptDir/src/lib-nodejs"
npm version $semverVersion --no-git-tag-version

# setting pyproject.toml version
$pyprojectPath = [IO.Path]::Combine($scriptDir, 'src', 'lib-python', 'pyproject.toml')

$pythonVersion = $semverVersion
if ($pythonVersion -match '-g') {
    $pythonVersion = $fourpartVersion -replace '^(\d+\.\d+\.\d+)\.(\d+)$', '${1}.dev${2}'
}
Write-Host "Python version to $pythonVersion"

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
Copy-Item -Path "$scriptDir/README_NUGET.md" -Destination "$scriptDir/src/lib-python/README.md" -Force

Set-Location $originalLocation

