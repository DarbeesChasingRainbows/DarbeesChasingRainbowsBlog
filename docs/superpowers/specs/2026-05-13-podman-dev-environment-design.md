# Local Dev Environment — Podman Compose Orchestration (Phase 12, Step 1)

**Status:** Design approved 2026-05-13
**Author:** Darbee (with Claude Code)
**Branch target:** `feature/podman-dev-environment` (off `feature/graph-backed-rag` or `master` — see Plan)
**Phase context:** First phase of the broader "local services orchestration" decomposition. Phase 1+2 from the decomposition are merged into this spec: ArangoDB + LM Studio probe + DAIS Bridge .NET gateway. Astro dev server containerization (Phase 3) and Obsidian-in-container (Phase 4) are deliberately deferred.

---

## 1. Problem

Today, bringing up the DAIS Bridge / Phase 11 memory layer locally requires a manual sequence: remember the exact `docker run arangodb:3.12 --vector-index` flag, separately verify LM Studio is up with the right token, then `dotnet run` the gateway. Each piece is independently brittle (Docker Desktop has wedged on this machine during this session; LM Studio outages aren't surfaced; gateway boots even when its deps aren't ready). There is no `docker-compose.yml`, no orchestration scripts, and no canonical bring-up path. Onboarding a fresh machine — or recovering from a wedged container — requires re-deriving the steps from the resume guide.

## 2. Goal

`podman compose --profile dev up -d` (wrapped behind `make up`) brings the full memory-stack dev environment online in one command, with restart-on-crash policies on every service, structured log access, and a health-check command that says whether each piece is actually reachable. `make down` tears it down. The orchestration is version-controlled and lives alongside the code it serves.

## 3. Non-goals

- Boot-time autostart (the user explicitly chose manual bring-up with restart-on-crash, not always-running)
- Containerizing the Astro dev server (Phase 3 of the decomposition; deferred)
- Containerizing Obsidian (Phase 4; deferred)
- Production deployment — the `prod` profile is for local smoke-testing parity with the dev container, not actual hosting
- CI integration — CI continues to use GitHub Actions service containers per the existing Phase 11 G2 plan
- Multi-platform image builds (linux/arm64)
- Containerizing LM Studio itself — LM Studio is a desktop GPU app; only a sidecar **probe** that polls it from inside the orchestration network is in scope

## 4. Constraints + context

- **Host:** Linux (Fedora 44+), rootless podman 4.4+ (or 5.x with built-in `compose` subcommand). The machine also has docker installed but Docker Desktop has been intermittently wedging — podman is the chosen runtime.
- **Existing services on host:** LM Studio listens on `:1234` (desktop app, manual start, requires Bearer token via `LMSTUDIO_API_KEY`). Audiobookshelf is already running on `:13378` under podman — coexistence required.
- **Phase 11 baseline:** 29/29 .NET tests pass against an `arangodb:3.12 --vector-index` container with `ARANGO_TEST_RUN=1`. This orchestration must preserve that test path unchanged.
- **DAIS Bridge code reads connection details:** today from `appsettings.json` (hardcoded `http://localhost:8529`). The Phase 11 A6 plan already moves these to env-var-first lookup; this spec depends on that pattern being in place, and uses it to inject service-name URLs inside the compose network.

## 5. Architecture overview

Four services in one `compose.yaml`:

```
┌─────────────────────────────────────────────────────────────┐
│  host machine (Fedora)                                      │
│                                                             │
│   ┌──────────────┐         ┌─────────────────────────┐      │
│   │  LM Studio   │←────────│ lm-probe (curl loop)    │      │
│   │  :1234 (host)│  HTTP   │ logs UP/DOWN every 30s  │      │
│   └──────────────┘         └─────────────────────────┘      │
│         ▲                                                   │
│         │ host.containers.internal                          │
│   ┌─────┴───────────────────────────────────────────────┐   │
│   │ podman compose network: darbees-dev (bridge)        │   │
│   │                                                     │   │
│   │  ┌─────────────┐         ┌───────────────────────┐  │   │
│   │  │  arango     │◄────────│ dais-bridge-{dev|prod}│  │   │
│   │  │  :8529      │         │  :5000 (host-mapped)  │  │   │
│   │  │  vol: data  │         └───────────────────────┘  │   │
│   │  └─────────────┘                                    │   │
│   │     ▲                                               │   │
│   └─────┼───────────────────────────────────────────────┘   │
│         │ host-mapped 8529                                  │
│   ┌─────┴────────┐                                          │
│   │ host: dotnet │                                          │
│   │ test runner  │                                          │
│   └──────────────┘                                          │
└─────────────────────────────────────────────────────────────┘
```

**Networking model:**
- Compose creates a default bridge network `darbees-dev`. `arango` and `dais-bridge` discover each other by service name (`http://arango:8529`).
- `arango`'s port 8529 is **also** host-mapped so host-side `dotnet test` runs (`ARANGO_TEST_URL=http://localhost:8529`) keep working unchanged.
- Containers reach LM Studio (on the host) via `http://host.containers.internal:1234`. Podman provides this hostname for rootless containers by default.
- `lm-probe` outputs to compose logs only — no exposed port.

**Persistence:**
- Named volume `arango-data` mounted at `/var/lib/arangodb3`. Survives `podman compose down`; only `make clean` (which runs `down -v`) removes it.

**Profile semantics:**
- `--profile dev` runs `arango`, `lm-probe`, `dais-bridge-dev` (mounted source + `dotnet watch`). Daily-driver.
- `--profile prod` runs `arango`, `lm-probe`, `dais-bridge-prod` (published binary). Pre-merge smoke / CI parity.
- `arango` and `lm-probe` have no profile assignment, so both profiles include them.

**Configuration / secrets:**
- `.env` at repo root (gitignored) holds `ARANGO_ROOT_PASSWORD` and `LMSTUDIO_API_KEY`.
- `.env.example` (committed) documents the keys.
- `compose.yaml` references via `${VAR}` expansion with defaults.
- DAIS Bridge env vars `ARANGO_URL`, `ARANGO_USER`, `ARANGO_PASSWORD`, `LMSTUDIO_URL`, `LMSTUDIO_API_KEY` are injected by compose; they override `appsettings.json` (per the Phase 11 A6 lookup priority).

## 6. `compose.yaml` (canonical structure)

File: `compose.yaml` at repo root.

```yaml
name: darbees-dev

services:
  arango:
    image: arangodb:3.12
    command: ["--vector-index"]
    environment:
      ARANGO_ROOT_PASSWORD: ${ARANGO_ROOT_PASSWORD:-password}
    ports:
      - "8529:8529"
    volumes:
      - arango-data:/var/lib/arangodb3
    healthcheck:
      test: ["CMD", "curl", "-fsS", "-u", "root:${ARANGO_ROOT_PASSWORD:-password}",
             "http://localhost:8529/_api/version"]
      interval: 5s
      timeout: 2s
      retries: 12
    restart: unless-stopped

  lm-probe:
    image: alpine:3.20
    command:
      - sh
      - -c
      - |
        apk add --no-cache curl >/dev/null
        while true; do
          if curl -fsS -m 3 -H "Authorization: Bearer ${LMSTUDIO_API_KEY:-}" \
              http://host.containers.internal:1234/v1/models >/dev/null 2>&1; then
            echo "[$(date -Iseconds)] lm-studio UP"
          else
            echo "[$(date -Iseconds)] lm-studio DOWN"
          fi
          sleep 30
        done
    environment:
      LMSTUDIO_API_KEY: ${LMSTUDIO_API_KEY:-}
    restart: unless-stopped

  dais-bridge-dev:
    profiles: ["dev"]
    build:
      context: ./dais-bridge
      target: dev
    command: ["dotnet", "watch", "run", "--no-launch-profile", "--project", "dais-bridge.csproj"]
    volumes:
      - ./dais-bridge:/src:Z
    working_dir: /src
    environment:
      ARANGO_URL: http://arango:8529
      ARANGO_USER: root
      ARANGO_PASSWORD: ${ARANGO_ROOT_PASSWORD:-password}
      LMSTUDIO_URL: http://host.containers.internal:1234
      LMSTUDIO_API_KEY: ${LMSTUDIO_API_KEY:-}
      DOTNET_USE_POLLING_FILE_WATCHER: "true"
    ports:
      - "5000:5000"
    depends_on:
      arango: { condition: service_healthy }
    restart: unless-stopped

  dais-bridge-prod:
    profiles: ["prod"]
    build:
      context: ./dais-bridge
      target: prod
    environment:
      ARANGO_URL: http://arango:8529
      ARANGO_USER: root
      ARANGO_PASSWORD: ${ARANGO_ROOT_PASSWORD:-password}
      LMSTUDIO_URL: http://host.containers.internal:1234
      LMSTUDIO_API_KEY: ${LMSTUDIO_API_KEY:-}
    ports:
      - "5000:5000"
    depends_on:
      arango: { condition: service_healthy }
    restart: unless-stopped

volumes:
  arango-data:
```

**Notable choices:**
- `name: darbees-dev` — explicit project name; sets the network name and container prefixes.
- No explicit `networks:` block — compose creates a default bridge.
- `:Z` SELinux label on the dev source mount (Fedora requirement).
- `DOTNET_USE_POLLING_FILE_WATCHER=true` — workaround for `dotnet watch` over volume mounts.
- `condition: service_healthy` on `depends_on` — dais-bridge waits for Arango's `/_api/version` to answer.
- `host.containers.internal` — podman default; if a future version drops it, add `extra_hosts: ["host.containers.internal:host-gateway"]`.

## 7. `dais-bridge/Dockerfile` (multi-stage)

```dockerfile
# syntax=docker/dockerfile:1.7

# ============================================================
# dev — SDK + dotnet watch
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dev
WORKDIR /src
COPY dais-bridge.csproj ./
RUN dotnet restore dais-bridge.csproj
ENV DOTNET_USE_POLLING_FILE_WATCHER=true \
    ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
CMD ["dotnet", "watch", "run", "--no-launch-profile", "--project", "dais-bridge.csproj"]

# ============================================================
# build — produce published binary
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY dais-bridge.csproj ./
RUN dotnet restore dais-bridge.csproj
COPY . ./
RUN dotnet publish dais-bridge.csproj \
    -c Release \
    -o /publish \
    --no-restore

# ============================================================
# prod — runtime image
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS prod
WORKDIR /app
COPY --from=build /publish ./
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
USER app
ENTRYPOINT ["dotnet", "dais-bridge.dll"]
```

Companion `dais-bridge/.dockerignore`:

```
bin/
obj/
*.user
.vs/
appsettings.Development.json
.dockerignore
Dockerfile
```

**Notable choices:**
- Build context is `./dais-bridge` — tests project is not included.
- Pre-restore in `dev` stage is purely a cold-start optimization; volume mount masks the result at runtime but the NuGet cache in `/root/.nuget/packages` persists.
- `USER app` in `prod` — non-root user shipped in `mcr.microsoft.com/dotnet/aspnet:9.0`.
- `.dockerignore` keeps host build artifacts out of the prod stage so it doesn't ship a runtime mismatch.

## 8. Helper Makefile

File: `Makefile` at repo root. Default goal is `help` (self-documenting via `##` comments on each target).

```makefile
.DEFAULT_GOAL := help
COMPOSE := podman compose

.PHONY: help up up-dev up-prod down restart build rebuild ps logs \
        logs-arango logs-lm logs-bridge shell-bridge shell-arango \
        health clean init

help:                              ## List available targets
	@awk 'BEGIN {FS = ":.*?## "} /^[a-zA-Z_-]+:.*?## / \
		{printf "  \033[36m%-15s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

init:                              ## First-time setup: ensure .env exists
	@test -f .env || (cp .env.example .env && \
		echo "Created .env from .env.example — fill in LMSTUDIO_API_KEY")

up: up-dev                         ## Alias for up-dev

up-dev: init                       ## Start dev stack
	$(COMPOSE) --profile dev up -d

up-prod: init                      ## Start prod stack
	$(COMPOSE) --profile prod up -d

down:                              ## Stop and remove all containers
	$(COMPOSE) --profile dev --profile prod down

restart:                           ## Restart all running services
	$(COMPOSE) restart

build:                             ## Build all images
	$(COMPOSE) --profile dev --profile prod build

rebuild:                           ## Force rebuild with no cache
	$(COMPOSE) --profile dev --profile prod build --no-cache

ps:                                ## Show service status
	$(COMPOSE) ps

logs:                              ## Tail all logs
	$(COMPOSE) logs -f --tail=50

logs-arango:                       ## Tail arangodb logs
	$(COMPOSE) logs -f --tail=50 arango

logs-lm:                           ## Tail lm-probe logs (LM Studio up/down)
	$(COMPOSE) logs -f --tail=50 lm-probe

logs-bridge:                       ## Tail dais-bridge logs
	$(COMPOSE) logs -f --tail=50 dais-bridge-dev dais-bridge-prod

shell-bridge:                      ## Open a shell inside the running dais-bridge container
	$(COMPOSE) exec dais-bridge-dev /bin/bash

shell-arango:                      ## Open arangosh inside arango container
	$(COMPOSE) exec arango arangosh \
		--server.endpoint http+tcp://localhost:8529 \
		--server.username root \
		--server.password $${ARANGO_ROOT_PASSWORD:-password}

health:                            ## Smoke-check that services are reachable
	@echo "--- ArangoDB:" && \
		curl -fsS -u root:$${ARANGO_ROOT_PASSWORD:-password} \
			http://localhost:8529/_api/version || echo "  DOWN"
	@echo "--- LM Studio (host):" && \
		curl -fsS -H "Authorization: Bearer $${LMSTUDIO_API_KEY}" \
			http://localhost:1234/v1/models >/dev/null && echo "  UP" || echo "  DOWN"
	@echo "--- DAIS Bridge (5000):" && \
		curl -fsS http://localhost:5000/ || echo "  DOWN"

clean:                             ## down + remove arango-data volume (DESTRUCTIVE)
	$(COMPOSE) --profile dev --profile prod down -v
	@echo "Volumes removed. Next 'make up' will start with an empty database."
```

`.env.example` (committed):

```bash
# Copy to .env (gitignored). The Makefile's `init` target does this for you.

# Local ArangoDB root password. Dev default is fine; pick anything for local.
ARANGO_ROOT_PASSWORD=password

# LM Studio API token. Generate one in LM Studio's developer settings.
# Required from Phase 11 Task A6 onward. Leave blank if you only need the
# arango stack and don't have LM Studio running yet.
LMSTUDIO_API_KEY=
```

## 9. Documentation impact

**New file:** `docs/dev-environment.md` — canonical "how to bring up the local stack" guide.

Contents:
- Prerequisites (podman ≥ 4.4, LM Studio installed for full functionality)
- First-time setup: `make init`, fill in `.env`, `make up`
- Daily workflow: `make up` / `make down` / `make health` / `make logs`
- Profile switching: dev vs prod
- Troubleshooting table (below)

**Files touched:**
- `README.md` — new "Run the local services" subsection linking to `docs/dev-environment.md`.
- `CLAUDE.md` — add `make` commands to the Commands section; refresh the LM Studio caveat.
- `OBSIDIAN-CONTENT-WORKFLOW.md` — short note: this orchestration is only required for backend / Phase 11 work; pure content authoring doesn't need it.
- `TODO-phase11.md` — replace the manual `docker run` cold-start step with `make up` + `make health`.
- `docs/superpowers/RESUME-graph-backed-rag.md` — update the Resume sequence environment block to use `make up`.
- `HANDOFF.md` — add a Phase 12 entry once the implementation lands (handled in the implementation plan, not this spec).

**Troubleshooting table for `docs/dev-environment.md`:**

| Symptom | Cause | Fix |
|---|---|---|
| `host.containers.internal` doesn't resolve | Podman version too old, or non-default network | Add `extra_hosts: ["host.containers.internal:host-gateway"]` to each service that needs it |
| `permission denied` reading mounted source on Fedora | SELinux | Confirm `:Z` on volume mount (in spec); reboot if `restorecon` is stuck |
| Port 8529 already in use on `make up` | Another container or process holding it | `podman ps` to find culprit; `podman rm -f <name>` if stale |
| `lm-probe` always logs DOWN | LM Studio not running, token wrong, or model not loaded | Confirm LM Studio open, model loaded, `LMSTUDIO_API_KEY` matches |
| `dotnet watch` doesn't pick up file changes | Polling not enabled | Confirm `DOTNET_USE_POLLING_FILE_WATCHER=true` in environment |
| `podman compose` not found | Old podman (< 4.4) | `sudo dnf install podman-compose`, or upgrade podman |
| Container exits 125 with "Operation not supported" on Arango volume | Rootless UID mapping mismatch | `podman unshare chown -R 999:999 <volume-path>` once after first `make up` |

## 10. Testing / verification plan

The implementation plan is "done" when these all pass:

1. **Cold-start dev profile**
   ```bash
   make clean && make up
   make health
   # Expect: ArangoDB UP, LM Studio UP (if running), DAIS Bridge UP
   ```

2. **Existing Phase 11 tests pass against the orchestrated stack**
   ```bash
   ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj
   # Expect: 29/29 PASS — baseline preserved
   ```

3. **`dotnet watch` detects changes inside the dev container**
   ```bash
   make up
   make logs-bridge   # in one terminal
   touch dais-bridge/Program.cs   # in another
   # Expect: watch logs show "File changed" within ~1s and a rebuild
   ```

4. **Restart-on-crash works**
   ```bash
   podman compose kill arango
   sleep 5
   make ps
   # Expect: arango is "Up" again
   ```

5. **`prod` profile builds and runs**
   ```bash
   make down
   podman compose --profile prod up -d
   make health
   # Expect: DAIS Bridge UP
   ```

6. **`make clean` is destructive but recoverable**
   ```bash
   make clean && make up
   # Expect: Arango boots with empty data; first EnsureSchemaAsync call re-creates collections
   ```

## 11. Migration / rollout

This is additive. Existing workflows continue to work:
- `dotnet run` from host against host-side ArangoDB still works (just less convenient).
- `npm run dev` / `npm run build` for the Astro side are unchanged.
- The 29/29 Phase 11 test baseline is preserved (verified by check #2).

**Steps to land:**
1. Land the Phase 11 A6 changes that env-var-ize the DAIS Bridge configuration (`ARANGO_URL`, `LMSTUDIO_API_KEY` lookup). If A6 is not yet merged when this work starts, this spec's plan picks up that change as a prerequisite.
2. Add the `compose.yaml`, `dais-bridge/Dockerfile`, `dais-bridge/.dockerignore`, `Makefile`, `.env.example` files.
3. Run the verification plan (Section 10).
4. Update docs (Section 9).
5. Open PR with the dev environment changes.

**Rollback:** all changes are additive files plus doc edits. Reverting the PR returns the project to today's `dotnet run + manual docker run` state.

## 12. Open questions deferred to implementation

- **Phase 11 A6 prerequisite:** is the gateway's env-var-first config lookup already merged when this work starts? If not, A6 lands first.
- **`podman compose` vs `podman-compose`:** podman 5.x ships `compose` as a subcommand; older podman uses the `podman-compose` Python wrapper. The Makefile uses `podman compose` (space); add a note in `docs/dev-environment.md` for podman-compose users.
- **`arango` UID inside the volume:** ArangoDB image runs as UID 999. Rootless podman may need a one-time `podman unshare chown` if the volume is created with wrong ownership. Verify on first `make up`; document if it surfaces.
- **`dais-bridge` startup probe inside container:** does the .NET app expose a `/health` endpoint? If not, `make health`'s `curl http://localhost:5000/` will hit `/` and rely on whatever that returns (per Phase 11's "Darbee Sovereign AI Gateway Active" response). A dedicated `/health` is a small follow-up.
- **CI alignment:** the existing Phase 11 G2 plan adds a GitHub Actions service container for ArangoDB. After this orchestration lands, decide whether CI should consume the same `compose.yaml` (cleaner) or keep its own service-container definition (simpler, parallel). Defer to G2 task.

## 13. Anti-patterns to avoid

- **Don't bake secrets into images.** `LMSTUDIO_API_KEY` is always read from env / `.env`, never copied into a Dockerfile layer.
- **Don't run as root in `prod`.** The `USER app` directive is required; reviewer should reject prod images without it.
- **Don't change DAIS Bridge connection-string logic to be container-specific.** The single env-var-first lookup must work both inside containers (URL = `http://arango:8529`) and on the host (URL = `http://localhost:8529`).
- **Don't add a service for LM Studio.** It's a desktop GPU app, not a service to orchestrate. The probe is the only LM-Studio-aware piece.

## 14. References

- Phase 11 spec: [docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md](2026-05-09-graph-backed-rag-design.md)
- Phase 11 plan: [docs/superpowers/plans/2026-05-09-graph-backed-rag.md](../plans/2026-05-09-graph-backed-rag.md)
- Phase 11 punchlist: [TODO-phase11.md](../../../TODO-phase11.md)
- Authoring workflow (Obsidian): [OBSIDIAN-CONTENT-WORKFLOW.md](../../../OBSIDIAN-CONTENT-WORKFLOW.md)
- Podman documentation: Context7 `/containers/podman`. Notable: `podman generate systemd` is deprecated in favor of Quadlets; for manual bring-up + restart-on-crash, regular compose files are the recommended path.
- Compose Spec (consumed by both podman compose and docker compose): https://compose-spec.io/
