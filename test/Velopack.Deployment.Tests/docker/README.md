# Local service stack for `Velopack.Deployment.Tests`

These tests exercise the real deployment/update-source code paths (Gitea, GitLab, Azure Blob, S3)
against a local docker stack instead of hitting live cloud services. Every test **probes its backing
service and skips (does not fail)** when that service is not running, so the suite is safe to run
without docker — you just get skips instead of coverage.

## Start / stop / reset

```bash
# Start everything (detached). Run once; leave it up across many test runs.
docker compose -f test/Velopack.Deployment.Tests/docker/docker-compose.yml up -d

# Check health / status
docker compose -f test/Velopack.Deployment.Tests/docker/docker-compose.yml ps

# Stop (keeps volumes/data)
docker compose -f test/Velopack.Deployment.Tests/docker/docker-compose.yml stop

# Full reset (removes containers AND volumes — wipes seeded users/tokens/repos)
docker compose -f test/Velopack.Deployment.Tests/docker/docker-compose.yml down -v
```

## Ports & credentials

| Service        | Container                   | Endpoint                  | Credentials |
|----------------|-----------------------------|---------------------------|-------------|
| Gitea 1.22     | `velopack-test-gitea-122`   | http://localhost:3122     | admin `velopack` / `VelopackTest123!` (seeded by tests) |
| Gitea 1.24     | `velopack-test-gitea-124`   | http://localhost:3124     | admin `velopack` / `VelopackTest123!` (seeded by tests) |
| Gitea latest   | `velopack-test-gitea-latest`| http://localhost:3199     | admin `velopack` / `VelopackTest123!` (seeded by tests) |
| GitLab CE      | `velopack-test-gitlab`      | http://localhost:8929     | root `root` / `VelopackTest123!`; PAT `glpat-velopacktests1234567890` (seeded by tests) |
| Azurite (blob) | `velopack-test-azurite`     | http://127.0.0.1:10000    | Azurite well-known dev account |
| S3Mock (S3)    | `velopack-test-s3mock`      | http://localhost:9090     | any credentials accepted |

These values are mirrored in `Infra/DockerServices.cs`, `Infra/GiteaAdmin.cs`, and
`Infra/GitLabAdmin.cs` — keep them in sync with `docker-compose.yml`.

### Seeding

The Gitea admin user and API token, and the GitLab PAT, are created automatically by the tests
(idempotently) the first time they run against a fresh stack:

- **Gitea** — `GiteaAdmin` runs `docker exec -u git <container> gitea admin user create ...` and then
  creates an API token over the REST API. Re-runs are no-ops.
- **GitLab** — `GitLabAdmin` runs `docker exec <container> gitlab-rails runner "<ruby>"` to create a
  personal access token with a **fixed, known value** (so tests can authenticate deterministically).
  This is skipped if the token already exists.

## GitLab first-boot wait

GitLab CE is heavy and can take **several minutes** to become healthy on first boot (the compose file
allows a 400s start-period). Everything else is up within seconds. Poll its health endpoint before
expecting GitLab tests to pass:

```bash
# Note: /-/health is loopback-allowlisted in-container and returns 404 from the host. Probe the
# login page (200 once ready, 502 while booting) and/or check the container health status.
curl -fsS http://localhost:8929/users/sign_in     # 200 when ready
docker compose -f test/Velopack.Deployment.Tests/docker/docker-compose.yml ps gitlab
```

Until GitLab is healthy, GitLab-backed tests skip with an actionable message. The first GitLab test
that runs also pays a one-time 30-60s cost for `gitlab-rails runner` PAT seeding.

## Windows & macOS CI (native services, no docker)

Windows CI runners cannot run Linux containers (the preinstalled docker daemon is Windows-containers
only — `docker run alpine` fails with "no matching manifest for windows/amd64", verified on
windows-latest — and `wsl --install` hangs provisioning non-interactively), and macOS runners have
no docker daemon at all. So on those runners, `start-native-services.ps1` runs the same services
**natively**: the Gitea binaries for the host platform (same three versions, same ports, admin
pre-seeded), Azurite via `npm install -g azurite` (with `--skipApiVersionCheck --loose`, matching
compose), and S3Mock's `-exec.jar` via `java`. GitLab has no Windows/macOS form, so GitLab-backed
tests skip there. The script is idempotent (services already answering on their port are left
alone), so it coexists with a running docker stack and works locally too — requires node/npm and
java on PATH (plus `xz` on macOS, brew-installed automatically if missing).

Local development doesn't need any of this: use Docker Desktop / Colima and the plain compose
commands above.

```powershell
# Windows PowerShell 5+ or pwsh 7+ (macOS)
./test/Velopack.Deployment.Tests/docker/start-native-services.ps1
```

## Running the tests

```bash
# All infra smoke tests
dotnet test test/Velopack.Deployment.Tests/Velopack.Deployment.Tests.csproj

# A single test (xunit v3 / Microsoft.Testing.Platform)
dotnet test --project test/Velopack.Deployment.Tests -- --filter-method '*GiteaServersSeedAndCreateRepo*'
```
