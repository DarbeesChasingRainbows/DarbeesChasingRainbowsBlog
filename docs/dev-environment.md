# Local Dev Environment

The DAIS Bridge memory-stack runs locally via `podman compose`. One command brings everything up; one command tears it down. Restart-on-crash on every service.

> Content authoring for darbeeschasingrainbows.com happens in Obsidian — see [`OBSIDIAN-CONTENT-WORKFLOW.md`](../OBSIDIAN-CONTENT-WORKFLOW.md). This dev environment is for backend / Phase 11 work (the DAIS Bridge gateway, the memory layer, etc.). If you're only editing `.mdx` posts, you don't need any of this.

## Prerequisites

- **podman ≥ 4.4** (Fedora 39+ ships 4.x; podman 5.x has built-in `compose` subcommand). On older podman, install `podman-compose` separately: `sudo dnf install podman-compose`.
- **GNU make** — comes with `make` on most Linux distros.
- **LM Studio** (desktop app) loaded with `nomic-embed-text-v1.5` (or compatible 768-dim embedding model), running with a Bearer token. Only required from Phase 11 Task A6 onward — earlier tasks don't need it.

## First-time setup

```bash
make init           # creates .env from .env.example
# Edit .env to fill in LMSTUDIO_API_KEY if LM Studio is running.
make up             # starts the dev stack
make health         # confirms each service is reachable
```

## Daily workflow

| Command | What it does |
|---|---|
| `make up` | Start dev stack (arango + lm-probe + dais-bridge-dev with hot reload) |
| `make up-prod` | Start prod stack (same arango + lm-probe, but the published-binary gateway) |
| `make down` | Stop and remove all containers (volumes preserved) |
| `make ps` | List running services |
| `make health` | Curl each service and report UP/DOWN |
| `make logs` | Tail all service logs |
| `make logs-bridge` | Tail just dais-bridge |
| `make logs-arango` | Tail just arangodb |
| `make logs-lm` | Tail the LM Studio probe sidecar |
| `make shell-bridge` | bash inside the running gateway container |
| `make shell-arango` | arangosh inside the running arangodb container |
| `make restart` | Restart all services |
| `make build` | Rebuild images (incremental) |
| `make rebuild` | Force rebuild with `--no-cache` |
| `make clean` | **Destructive.** down + remove `arango-data` volume. Next `make up` boots with an empty database. |

## What's in the stack

| Service | Image | Ports | Notes |
|---|---|---|---|
| `arango` | `arangodb:3.12` | `8529:8529` | Started with `--vector-index` flag. Named volume `arango-data` persists between runs. |
| `lm-probe` | `alpine:3.20` | (none) | Sidecar that polls `host.containers.internal:1234/v1/models` every 30s and logs UP/DOWN. View with `make logs-lm`. |
| `dais-bridge-dev` | `mcr.microsoft.com/dotnet/sdk:9.0` | `5000:5000` | Source-mounted + `dotnet watch`. Hot reload on file changes. |
| `dais-bridge-prod` | `mcr.microsoft.com/dotnet/aspnet:9.0` | `5000:5000` | Published Release binary, runs as non-root `app` user. |

## Profiles

- `--profile dev` (default for `make up`): source-mounted gateway with hot reload.
- `--profile prod`: published-binary gateway for smoke-testing parity.
- `arango` and `lm-probe` run under both profiles.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `host.containers.internal` doesn't resolve | Podman version too old, or non-default network | Add `extra_hosts: ["host.containers.internal:host-gateway"]` to each service that needs it |
| `permission denied` reading mounted source on Fedora | SELinux | Confirm `:Z` on volume mount (in spec); reboot if `restorecon` is stuck |
| Port 8529 already in use on `make up` | Another container or process holding it | `podman ps` to find culprit; `podman rm -f <name>` if stale |
| `lm-probe` always logs DOWN | LM Studio not running, token wrong, or model not loaded | Confirm LM Studio open, model loaded, `LMSTUDIO_API_KEY` matches |
| `dotnet watch` doesn't pick up file changes | Polling not enabled | Confirm `DOTNET_USE_POLLING_FILE_WATCHER=true` in environment |
| `podman compose` not found | Old podman (< 4.4) | `sudo dnf install podman-compose`, or upgrade podman |
| Container exits 125 with "Operation not supported" on Arango volume | Rootless UID mapping mismatch | `podman unshare chown -R 999:999 <volume-path>` once after first `make up` |
