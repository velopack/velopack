# Starts the deployment-test docker services on a Windows runner by hosting Linux docker inside
# WSL2, which works out of the box on Windows Server 2025 (the current windows-latest image).
# Uses the same docker-compose.yml as Linux; the Windows host reaches the containers through
# WSL2's localhost port forwarding.
#
# GitLab is intentionally excluded here: it takes several minutes to boot, and its tests are fully
# covered by the Linux CI leg — they skip on Windows with an actionable message.
#
# The tests' docker-exec seeding fallback cannot reach the WSL dockerd from the Windows side, so
# this script pre-seeds the gitea admin user; the tests' probe-first REST path handles the rest.
# Credentials must stay in sync with Infra/GiteaAdmin.cs.

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$services = @("gitea-122", "gitea-124", "gitea-latest", "azurite", "s3mock")
$giteaContainers = @("velopack-test-gitea-122", "velopack-test-gitea-124", "velopack-test-gitea-latest")

function Test-Endpoint([string]$url) {
    # Any HTTP response (including 4xx, e.g. azurite's 403 on unauthenticated requests) proves a
    # live listener; only connection failures count as down.
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
            Write-Host "$name is reachable at $url"
            return
        }
        Start-Sleep -Seconds 2
    }
    throw "$name did not become reachable at $url within ${timeoutSec}s"
}

if (Test-Endpoint "http://localhost:3122/api/healthz") {
    Write-Host "Services already appear to be running; nothing to do."
    exit 0
}

Write-Host "Installing Ubuntu WSL2 distro..."
wsl --install Ubuntu
if ($LASTEXITCODE -ne 0) { throw "wsl --install Ubuntu failed with exit code $LASTEXITCODE" }
wsl --list --verbose

# Keep the WSL VM alive between commands/steps — it otherwise shuts down shortly after the last
# client exits, taking the containers with it.
wsl -d Ubuntu --exec dbus-launch true

Write-Host "Installing docker inside WSL..."
wsl -d Ubuntu -u root -- sh -c "curl -fsSL https://get.docker.com | sh"
if ($LASTEXITCODE -ne 0) { throw "docker installation inside WSL failed with exit code $LASTEXITCODE" }
wsl -d Ubuntu -u root -- sh -c "service docker status >/dev/null 2>&1 || service docker start"
if ($LASTEXITCODE -ne 0) { throw "starting dockerd inside WSL failed with exit code $LASTEXITCODE" }

Write-Host "Starting services: $($services -join ', ') (gitlab excluded on Windows)..."
# --cd accepts the Windows path and maps it to /mnt/... inside WSL.
wsl -d Ubuntu -u root --cd "$PSScriptRoot" -- docker compose -f docker-compose.yml up -d --wait --wait-timeout 600 @services
if ($LASTEXITCODE -ne 0) {
    wsl -d Ubuntu -u root --cd "$PSScriptRoot" -- docker compose -f docker-compose.yml ps -a
    wsl -d Ubuntu -u root --cd "$PSScriptRoot" -- docker compose -f docker-compose.yml logs --tail 50
    throw "docker compose up failed with exit code $LASTEXITCODE"
}

Write-Host "Pre-seeding gitea admin users..."
foreach ($container in $giteaContainers) {
    $output = wsl -d Ubuntu -u root -- docker exec -u git $container gitea admin user create --admin `
        --username velopack --password VelopackTest123! --email velopack@example.com --must-change-password=false 2>&1
    if ($LASTEXITCODE -ne 0 -and "$output" -notmatch "already exists") {
        throw "seeding admin user in $container failed: $output"
    }
}

Write-Host "Verifying Windows-host reachability through WSL localhost forwarding..."
Wait-Endpoint "gitea-122" "http://localhost:3122/api/healthz"
Wait-Endpoint "gitea-124" "http://localhost:3124/api/healthz"
Wait-Endpoint "gitea-latest" "http://localhost:3199/api/healthz"
Wait-Endpoint "azurite" "http://127.0.0.1:10000/devstoreaccount1?comp=list"
Wait-Endpoint "s3mock" "http://localhost:9090/"

Write-Host ""
Write-Host "All deployment-test services are up in WSL2 docker (gitlab excluded; its tests skip on Windows)."
