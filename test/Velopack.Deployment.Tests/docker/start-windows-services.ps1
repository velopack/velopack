# Starts the deployment-test services natively on Windows, where Linux docker containers are not
# available (Windows CI runners have no nested virtualization). Mirrors docker-compose.yml:
#   gitea 1.22 -> http://localhost:3122      azurite blob -> http://localhost:10000
#   gitea 1.24 -> http://localhost:3124      s3mock       -> http://localhost:9090
#   gitea latest -> http://localhost:3199    (gitlab has no Windows form and stays unavailable)
#
# Idempotent: services already responding on their port are left alone, so this coexists with a
# running docker compose stack and can be re-run freely. Ports/credentials must stay in sync with
# docker-compose.yml and Infra/DockerServices.cs / Infra/GiteaAdmin.cs.
#
# Requires: PowerShell 5+, node/npm (azurite), java 17+ (s3mock).

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$cacheDir = Join-Path $PSScriptRoot "..\obj\native-services"
New-Item -ItemType Directory -Force $cacheDir | Out-Null
$cacheDir = (Resolve-Path $cacheDir).Path

$giteaAdminUser = "velopack"
$giteaAdminPassword = "VelopackTest123!"
$giteaAdminEmail = "velopack@example.com"
$s3mockVersion = "5.1.0"

function Test-Endpoint([string]$url) {
    # Any HTTP response (including 4xx, e.g. azurite's 403 on unauthenticated requests) proves a
    # live listener; only connection failures count as down. Works on both PS 5.1 and pwsh 7.
    try {
        Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3 | Out-Null
        return $true
    } catch {
        return $null -ne $_.Exception.Response
    }
}

function Wait-Endpoint([string]$name, [string]$url, [int]$timeoutSec = 90) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Endpoint $url) {
            Write-Host "$name is up at $url"
            return
        }
        Start-Sleep -Seconds 2
    }
    throw "$name did not become healthy at $url within ${timeoutSec}s"
}

function Get-CachedFile([string]$url, [string]$destPath) {
    if (-not (Test-Path $destPath)) {
        Write-Host "Downloading $url"
        $tmp = "$destPath.download"
        Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing
        Move-Item $tmp $destPath -Force
    }
}

function Start-Gitea([string]$version, [int]$port) {
    $health = "http://localhost:$port/api/healthz"
    if (Test-Endpoint $health) {
        Write-Host "gitea $version already running on port $port, skipping"
        return
    }

    $exe = Join-Path $cacheDir "gitea-$version.exe"
    Get-CachedFile "https://dl.gitea.com/gitea/$version/gitea-$version-windows-4.0-amd64.exe" $exe

    $workDir = Join-Path $cacheDir "gitea-$version-data"
    $confDir = Join-Path $workDir "custom\conf"
    New-Item -ItemType Directory -Force $confDir | Out-Null
    $appIni = Join-Path $confDir "app.ini"
    if (-not (Test-Path $appIni)) {
        @"
RUN_MODE = prod

[server]
HTTP_PORT = $port
ROOT_URL = http://localhost:$port/
OFFLINE_MODE = true

[database]
DB_TYPE = sqlite3

[security]
INSTALL_LOCK = true

[log]
LEVEL = Warn
"@ | Out-File $appIni -Encoding utf8
    }

    $env:GITEA_WORK_DIR = $workDir
    # Initialize the DB and create the admin user BEFORE starting the server: the gitea CLI opens the
    # sqlite database directly and would contend with a running server for the write lock.
    & $exe migrate --config $appIni 2>&1 | Out-Null
    $createOutput = & $exe admin user create --config $appIni --admin --username $giteaAdminUser `
        --password $giteaAdminPassword --email $giteaAdminEmail --must-change-password=false 2>&1
    if ($LASTEXITCODE -ne 0 -and "$createOutput" -notmatch "already exists") {
        throw "gitea $version admin user create failed: $createOutput"
    }

    Start-Process -FilePath $exe -ArgumentList "web", "--config", $appIni -WorkingDirectory $workDir -WindowStyle Hidden
    Remove-Item Env:GITEA_WORK_DIR
    Wait-Endpoint "gitea $version" $health
}

function Start-Azurite {
    # Probing "/" of azurite returns 400 (invalid request), which Test-Endpoint counts as alive.
    $probe = "http://127.0.0.1:10000/devstoreaccount1?comp=list"
    if (Test-Endpoint $probe) {
        Write-Host "azurite already running on port 10000, skipping"
        return
    }

    # npm creates .ps1/.cmd/sh shims; Start-Process can only execute the .cmd one.
    $shim = Get-Command azurite-blob.cmd -ErrorAction SilentlyContinue
    if (-not $shim) {
        Write-Host "Installing azurite via npm"
        npm install -g azurite | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "npm install -g azurite failed" }
        $npmBin = (& npm prefix -g).Trim()
        $shim = Get-Command (Join-Path $npmBin "azurite-blob.cmd") -ErrorAction SilentlyContinue
        if (-not $shim) { throw "azurite-blob.cmd not found after npm install -g azurite" }
    }

    $dataDir = Join-Path $cacheDir "azurite-data"
    New-Item -ItemType Directory -Force $dataDir | Out-Null
    # --skipApiVersionCheck/--loose mirror docker-compose.yml (newer Azure SDKs send API versions
    # azurite does not know yet).
    Start-Process -FilePath $shim.Source `
        -ArgumentList "--blobHost", "127.0.0.1", "--blobPort", "10000", "--location", $dataDir, "--silent", "--skipApiVersionCheck", "--loose" `
        -WindowStyle Hidden
    Wait-Endpoint "azurite" $probe
}

function Start-S3Mock {
    $probe = "http://localhost:9090/"
    if (Test-Endpoint $probe) {
        Write-Host "s3mock already running on port 9090, skipping"
        return
    }

    $java = Get-Command java -ErrorAction SilentlyContinue
    if (-not $java) { throw "java is required to run S3Mock natively but was not found on PATH" }

    $jar = Join-Path $cacheDir "s3mock-$s3mockVersion-exec.jar"
    Get-CachedFile "https://repo1.maven.org/maven2/com/adobe/testing/s3mock/$s3mockVersion/s3mock-$s3mockVersion-exec.jar" $jar
    Start-Process -FilePath $java.Source -ArgumentList "-jar", $jar -WindowStyle Hidden
    Wait-Endpoint "s3mock" $probe -timeoutSec 120
}

Start-Gitea "1.22.6" 3122
Start-Gitea "1.24.7" 3124
$latest = (Invoke-RestMethod "https://dl.gitea.com/gitea/version.json").latest.version
Start-Gitea $latest 3199
Start-Azurite
Start-S3Mock

Write-Host ""
Write-Host "All native deployment-test services are up (gitlab is unavailable on Windows; its tests skip)."
